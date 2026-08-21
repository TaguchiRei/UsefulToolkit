using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 自分自身のTransformを刃(切断平面)として、範囲内のCuttableObjectを一括切断するコンポーネント。
    /// transform.position が平面上の点、transform.up が平面の法線になる。
    /// </summary>
    public class MultiCutBlade : MonoBehaviour
    {
        [SerializeField, Tooltip("破片への結果反映を次フレームへ送るまでの1フレーム許容時間(ms)")]
        private float _LimitMs = 5;

        [SerializeField] private MeshCutObjectPool _pool;

        [SerializeField, Tooltip("各処理段階の所要時間をConsoleへ出力する")]
        private bool _enableProfileLog;

        private readonly MultiMeshCut _slicer = new();

        private void Awake()
        {
            _slicer.EnableProfileLog = _enableProfileLog;
        }

        [ContextMenu("切断")]
        private async void Test()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 halfExtents = box.size * 0.5f;
            Quaternion orientation = box.transform.rotation;
            Collider[] hits = Physics.OverlapBox(center, halfExtents, orientation);

            List<CuttableObject> cuttables = new List<CuttableObject>();
            HashSet<GameObject> addedObjects = new HashSet<GameObject>();
            foreach (Collider hit in hits)
            {
                GameObject obj = hit.gameObject;

                if (addedObjects.Contains(obj))
                    continue; // 既に追加済みならスキップ

                CuttableObject cuttable = obj.GetComponent<CuttableObject>();

                // 1回だけ切断可能なオブジェクトから生まれた破片は、もう切れない
                if (cuttable != null && cuttable.IsCuttable)
                {
                    cuttables.Add(cuttable);
                    addedObjects.Add(obj); // 追加済みとして記録
                }
            }

            if (cuttables.Count > 0)
            {
                await ExecuteCut(cuttables.ToArray());
            }
            else
            {
                Debug.Log("[UsefulToolkit.MeshCut] 範囲内にCuttableObjectが見つかりませんでした");
            }
        }

        /// <summary>
        /// 指定した複数のオブジェクトを一枚の刃で一括切断します
        /// </summary>
        public async UniTask ExecuteCut(CuttableObject[] targets)
        {
            if (targets == null || targets.Length == 0) return;

            targets = FilterCuttable(targets);
            if (targets.Length == 0) return;

            // プールの事前生成は非同期のため、完了前に切断すると破片が取得できない
            await _pool.WaitForGeneration();

            Stopwatch st = Stopwatch.StartNew();

            // 自分自身をBladeにする
            NativePlane blade = new NativePlane(transform.position, transform.up);

            // 切断を実行
            await _slicer.Cut(targets, blade);

            // プールから必要な数だけ破片オブジェクトを一括取得
            // ターゲット1つにつき前後2つの破片が必要
            int requiredCount = targets.Length * 2;
            var fragmentStubs = _pool.GetObjects(requiredCount);

            if (fragmentStubs.Count < requiredCount)
            {
                Debug.LogError(
                    $"[UsefulToolkit.MeshCut] 破片が不足しています。必要数 {requiredCount} に対し取得数 {fragmentStubs.Count}。プールの生成数を増やしてください。");
                return;
            }

            Stopwatch frameStopwatch = Stopwatch.StartNew();

            // 4. 結果を各破片に反映
            for (int i = 0; i < targets.Length; i++)
            {
                var target = targets[i];

                // Front側 (index: i*2)
                var frontData = fragmentStubs[i * 2];
                ApplyResult(frontData, _slicer.CutMesh[i * 2], _slicer.SamplingPoints[i * 2], target,
                    _slicer.FragmentMeshIds[i * 2]);

                // Back側 (index: i*2 + 1)
                var backData = fragmentStubs[i * 2 + 1];
                ApplyResult(backData, _slicer.CutMesh[i * 2 + 1], _slicer.SamplingPoints[i * 2 + 1], target,
                    _slicer.FragmentMeshIds[i * 2 + 1]);

                // 元のオブジェクトは消費済み。非アクティブ化し、二度と切断対象にならないようにする
                target.DisableCutting();
                target.gameObject.SetActive(false);

                // 切断元が破片だった場合、スロットを塞いだままにしないようプールへ返す。
                // 今回配った破片そのものだった場合(プールが一周した場合)は返してはいけない
                if (!fragmentStubs.Contains(target))
                {
                    _pool.TryReleaseObject(target);
                }

                await CheckTime(frameStopwatch, _LimitMs);
            }

            if (_enableProfileLog)
            {
                Debug.Log($"[UsefulToolkit.MeshCut] 切断から反映までの全体処理時間 {st.ElapsedMilliseconds}ms");
            }
        }

        /// <param name="fragmentMeshId">
        /// 再切断用にストアへ登録されたメッシュID。登録されていない(＝もう切れない)場合は -1。
        /// </param>
        private void ApplyResult(
            CuttableObject cuttable,
            Mesh mesh,
            List<Vector3> samplingPoints,
            CuttableObject original,
            int fragmentMeshId)
        {
            GameObject fragObj = cuttable.gameObject;

            // Transform同期
            fragObj.transform.SetPositionAndRotation(
                original.transform.position,
                original.transform.rotation
            );
            fragObj.transform.localScale = original.transform.localScale;

            // メッシュ設定
            cuttable.Mesh.sharedMesh = mesh;

            // マテリアルコピー処理
            var originalRenderer = original.Renderer;
            var fragmentRenderer = cuttable.Renderer;

            if (originalRenderer != null && fragmentRenderer != null)
            {
                Material[] originalMaterials = originalRenderer.sharedMaterials;

                // 断面サブメッシュは常に最後。未切断のメッシュを切ったときだけ1つ増え、
                // 既に断面を持つ破片を切り直したときは同じ数のままになる
                int subMeshCount = mesh.subMeshCount;
                Material[] newMaterials = new Material[subMeshCount];

                for (int i = 0; i < subMeshCount && i < originalMaterials.Length; i++)
                {
                    newMaterials[i] = originalMaterials[i];
                }

                newMaterials[^1] = cuttable.CapMaterial;

                fragmentRenderer.sharedMaterials = newMaterials;
            }

            // アクティブ化
            fragObj.SetActive(true);

            cuttable.SetupCollider(samplingPoints);

            // 切断可否の引き継ぎ。何回でも切断可能なものだけが新しいMeshIdを持つ
            cuttable.InheritCutSettings(original);

            if (fragmentMeshId >= 0)
            {
                cuttable.SetRegisteredMesh(fragmentMeshId);
                MeshDataCache.Instance.RegisterUser(cuttable);
            }
            else
            {
                cuttable.DisableCutting();
            }

            // 物理初速の継承
            if (original.Rig && cuttable.Rig)
            {
                cuttable.Rig.linearVelocity = original.Rig.linearVelocity;
                cuttable.Rig.angularVelocity = original.Rig.angularVelocity;
            }
        }

        /// <summary> 切断できないオブジェクトを除外します。 </summary>
        private static CuttableObject[] FilterCuttable(CuttableObject[] targets)
        {
            var result = new List<CuttableObject>(targets.Length);

            foreach (CuttableObject target in targets)
            {
                if (target == null) continue;

                if (!target.IsCuttable)
                {
                    Debug.LogWarning($"[UsefulToolkit.MeshCut] {target.name} は既に切断済みのため除外しました。");
                    continue;
                }

                result.Add(target);
            }

            return result.ToArray();
        }

        private async UniTask CheckTime(Stopwatch stopwatch, float limitMs = 5f)
        {
            if (stopwatch.ElapsedMilliseconds > limitMs)
            {
                await UniTask.Yield();
                stopwatch.Restart();

                if (_enableProfileLog)
                {
                    Debug.Log("[UsefulToolkit.MeshCut] 処理時間が長すぎたため、次のフレームに送りました。");
                }
            }
        }


#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            float _planeSize = 10.0f;
            int _gridCount = 10;

            Vector3 planePos = transform.position;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;

            Color _planeColor = new(0f, 1f, 1f, 0.15f);
            Color _outlineColor = Color.cyan;
            Color _gridColor = new(0f, 1f, 1f, 0.3f);

            // デプス(Zテスト)を有効にして描画
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            // === 中央(基準サイズ)の平面 ===
            Vector3 r = right * _planeSize;
            Vector3 f = forward * _planeSize;

            Vector3 p1 = planePos + r + f;
            Vector3 p2 = planePos + r - f;
            Vector3 p3 = planePos - r - f;
            Vector3 p4 = planePos - r + f;

            UnityEditor.Handles.color = _planeColor;
            UnityEditor.Handles.DrawSolidRectangleWithOutline(
                new[] { p1, p2, p3, p4 },
                _planeColor,
                _outlineColor
            );

            // === グリッド線 ===
            UnityEditor.Handles.color = _gridColor;
            for (int i = 1; i < _gridCount; i++)
            {
                float t = i / (float)_gridCount;
                Vector3 startH = Vector3.Lerp(p4, p1, t);
                Vector3 endH = Vector3.Lerp(p3, p2, t);
                UnityEditor.Handles.DrawLine(startH, endH);

                Vector3 startV = Vector3.Lerp(p1, p2, t);
                Vector3 endV = Vector3.Lerp(p4, p3, t);
                UnityEditor.Handles.DrawLine(startV, endV);
            }

            DrawOutline(planePos, right, forward, _planeSize, Color.green);

            DrawOutline(planePos, right, forward, _planeSize * 1.5f, Color.green);

            DrawOutline(planePos, right, forward, _planeSize * 0.5f, Color.green);

            DrawOutline(planePos, right, forward, _planeSize * 0.25f, Color.green);

            // Zテスト設定を戻す
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        }

        /// <summary>
        /// 任意サイズの外枠を描画する補助メソッド
        /// </summary>
        private void DrawOutline(Vector3 center, Vector3 right, Vector3 forward, float size, Color color)
        {
            Vector3 r = right * size;
            Vector3 f = forward * size;

            Vector3 p1 = center + r + f;
            Vector3 p2 = center + r - f;
            Vector3 p3 = center - r - f;
            Vector3 p4 = center - r + f;

            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawLine(p1, p2);
            UnityEditor.Handles.DrawLine(p2, p3);
            UnityEditor.Handles.DrawLine(p3, p4);
            UnityEditor.Handles.DrawLine(p4, p1);
        }

#endif
    }
}
