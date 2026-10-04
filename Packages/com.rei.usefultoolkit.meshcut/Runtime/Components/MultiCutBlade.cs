using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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

        [SerializeField, Tooltip("各処理段階の所要時間を計測し、切断ごとに表としてConsoleへ出力する")]
        private bool _enableProfileLog;

        private readonly MultiMeshCut _slicer = new();

        /// <summary> 切断ごとに計測結果を表としてConsoleへ出力するか </summary>
        public bool EnableProfileLog
        {
            get => _enableProfileLog;
            set => _enableProfileLog = value;
        }

        /// <summary> trueにすると、Consoleへは出力せずに計測だけを行い LastProfile を更新します。 </summary>
        public bool CollectProfile { get; set; }

        /// <summary> 計測が有効な状態で最後に完了した ExecuteCut の計測結果。未計測なら null。 </summary>
        public MeshCutProfile LastProfile { get; private set; }

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
                Vector3 bladePosition = transform.position;
                Vector3 bladeNormal = transform.up;

                MultiCutResult[] results = await ExecuteCut(cuttables.ToArray());
                LogResults(results, bladePosition, bladeNormal);
            }
            else
            {
                Debug.Log("[UsefulToolkit.MeshCut] 範囲内にCuttableObjectが見つかりませんでした");
            }
        }

        /// <summary>
        /// 切断結果の組ごとに、元の対象と表裏の破片の名前・アクティブ状態と、
        /// 各破片の Renderer の中心が刃の平面のどちら側にあるかを Console へ出力します。
        /// </summary>
        private static void LogResults(MultiCutResult[] results, Vector3 bladePosition, Vector3 bladeNormal)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"[UsefulToolkit.MeshCut] 切断結果 {results.Length} 組");

            for (int i = 0; i < results.Length; i++)
            {
                MultiCutResult result = results[i];
                builder.AppendLine(
                    $"  [{i}] 元: {DescribeObject(result.Original)} / 表: {DescribeFragment(result.Front, bladePosition, bladeNormal)} / 裏: {DescribeFragment(result.Back, bladePosition, bladeNormal)}");
            }

            Debug.Log(builder.ToString());
        }

        private static string DescribeObject(CuttableObject obj)
        {
            if (obj == null) return "null";

            return $"{obj.name}(active={obj.gameObject.activeSelf}, cuttable={obj.IsCuttable})";
        }

        /// <summary>
        /// 破片の説明に、有効な球コライダーの数と、Renderer の中心が刃の法線の側(+)か反対の側(-)かを付け足します。
        /// </summary>
        private static string DescribeFragment(CuttableObject fragment, Vector3 bladePosition, Vector3 bladeNormal)
        {
            if (fragment == null || fragment.Renderer == null) return DescribeObject(fragment);

            int enabledColliders = 0;
            foreach (SphereCollider col in fragment.GetComponents<SphereCollider>())
            {
                if (col.enabled) enabledColliders++;
            }

            float side = Vector3.Dot(fragment.Renderer.bounds.center - bladePosition, bladeNormal);
            return $"{DescribeObject(fragment)}[colliders={enabledColliders}, side={(side >= 0f ? "+" : "-")}]";
        }

        /// <summary>
        /// 指定した複数のオブジェクトを一枚の刃で一括切断します
        /// </summary>
        /// <returns>
        /// 実際に切断した対象ごとの、元の対象と表裏の破片の組。並び順は切断できない対象を除いた後の targets の順。
        /// すべての破片への反映が終わってから返します。何も切断しなかった場合は空の配列(null ではない)。
        /// </returns>
        public async UniTask<MultiCutResult[]> ExecuteCut(CuttableObject[] targets)
        {
            if (targets == null || targets.Length == 0) return Array.Empty<MultiCutResult>();

            targets = FilterCuttable(targets);
            if (targets.Length == 0) return Array.Empty<MultiCutResult>();

            MeshCutProfiler profiler = _enableProfileLog || CollectProfile
                ? new MeshCutProfiler()
                : MeshCutProfiler.Disabled;

            // 破片反映はフレームをまたぐため Persistent で確保し、finally で解放する
            NativeArray<float4> colliderSpheres = default;
            NativeArray<int> colliderSphereStart = default;

            try
            {
                // プールの事前生成は非同期のため、完了前に切断すると破片が取得できない
                int poolWaitStage = profiler.Request("プール生成待ち", MeshCutStageKind.Main);
                await _pool.WaitForGeneration();
                profiler.MarkStart(poolWaitStage);
                profiler.MarkEnd(poolWaitStage);
                profiler.Observe(poolWaitStage);

                // 自分自身をBladeにする
                NativePlane blade = new NativePlane(transform.position, transform.up);

                // 切断を実行
                await _slicer.Cut(targets, blade, profiler);

                // プールから必要な数だけ破片オブジェクトを一括取得
                // ターゲット1つにつき前後2つの破片が必要
                int getStage = profiler.BeginMain("破片取得");

                // 切断元がプールの破片だと、GetObjects でリサイクル(非アクティブ化)されたうえ、
                // 反映中に別の対象の破片として上書きされることがある。
                // そのため、反映で読む切断元の値は GetObjects の前に取っておく
                var originals = new OriginalSnapshot[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    originals[i] = new OriginalSnapshot(targets[i]);
                }

                int requiredCount = targets.Length * 2;
                var fragmentStubs = _pool.GetObjects(requiredCount);
                profiler.EndMain(getStage);

                if (fragmentStubs.Count < requiredCount)
                {
                    Debug.LogError(
                        $"[UsefulToolkit.MeshCut] 破片が不足しています。必要数 {requiredCount} に対し取得数 {fragmentStubs.Count}。プールの生成数を増やしてください。");
                    return Array.Empty<MultiCutResult>();
                }

                var results = new MultiCutResult[targets.Length];

                // 全破片の球コライダーを ColliderClusterJob でまとめて求める。
                // 設定値は破片側(プールの CuttableObject)のものを使うため、破片を取得した後に行う
                ComputeColliderSpheres(fragmentStubs, _slicer.SamplingPoints, profiler,
                    out colliderSpheres, out colliderSphereStart);

                Stopwatch frameStopwatch = Stopwatch.StartNew();

                long applyStart = Stopwatch.GetTimestamp();
                long applyTicks = 0;
                long colliderTicks = 0;
                int yieldCount = 0;

                // 4. 結果を各破片に反映
                for (int i = 0; i < targets.Length; i++)
                {
                    long itemStart = Stopwatch.GetTimestamp();

                    var target = targets[i];

                    // Front側 (index: i*2)
                    var frontData = fragmentStubs[i * 2];
                    ApplyResult(frontData, _slicer.CutMesh[i * 2], colliderSpheres, colliderSphereStart[i * 2],
                        originals[i], _slicer.FragmentMeshIds[i * 2], ref colliderTicks);

                    // Back側 (index: i*2 + 1)
                    var backData = fragmentStubs[i * 2 + 1];
                    ApplyResult(backData, _slicer.CutMesh[i * 2 + 1], colliderSpheres, colliderSphereStart[i * 2 + 1],
                        originals[i], _slicer.FragmentMeshIds[i * 2 + 1], ref colliderTicks);

                    // 切断元が今回配った破片そのもの(プールが一周した場合)で、既に破片として反映済みなら、
                    // 新しい破片として生きているので触らない
                    int reusedIndex = fragmentStubs.IndexOf(target);
                    if (reusedIndex < 0 || reusedIndex > i * 2 + 1)
                    {
                        // 元のオブジェクトは消費済み。非アクティブ化し、二度と切断対象にならないようにする。
                        // 後で破片として反映される場合は、その反映で再びアクティブ化・切断可否の設定が行われる
                        target.DisableCutting();
                        target.gameObject.SetActive(false);
                    }

                    // 切断元が破片だった場合、スロットを塞いだままにしないようプールへ返す。
                    // 今回配った破片そのものだった場合は返してはいけない
                    if (reusedIndex < 0)
                    {
                        _pool.TryReleaseObject(target);
                    }

                    applyTicks += Stopwatch.GetTimestamp() - itemStart;

                    results[i] = new MultiCutResult(target, frontData, backData);

                    if (await CheckTime(frameStopwatch, _LimitMs))
                    {
                        yieldCount++;
                    }
                }

                long applyEnd = Stopwatch.GetTimestamp();

                profiler.AddAccumulated("破片反映", applyStart, applyEnd, applyTicks);
                profiler.AddAccumulated("コライダー適用", applyStart, applyEnd, colliderTicks, isBreakdown: true);
                profiler.AddInfo("破片反映のフレーム分割回数", yieldCount);

                if (profiler.Enabled)
                {
                    LastProfile = profiler.Build("MultiCutBlade.ExecuteCut");

                    if (_enableProfileLog)
                    {
                        Debug.Log(LastProfile.ToString());
                    }
                }

                return results;
            }
            finally
            {
                if (colliderSpheres.IsCreated) colliderSpheres.Dispose();
                if (colliderSphereStart.IsCreated) colliderSphereStart.Dispose();

                profiler.Dispose();
            }
        }

        /// <summary>
        /// 破片ごとのサンプリング点と、破片自身のコライダー設定値から ColliderClusterJob で球を求めます。
        /// 破片 k の球は spheres[sphereStart[k]] から ColliderSettings.ClusterCount 個並びます。
        /// 破片反映の直前に必要なため、Jobの完了をその場で待ちます(破片単位で並列に処理されます)。
        /// 返す2つの配列は Persistent で確保しているので、呼び出し側で Dispose してください。
        /// </summary>
        private static void ComputeColliderSpheres(
            List<CuttableObject> fragments,
            List<List<Vector3>> samplingPoints,
            MeshCutProfiler profiler,
            out NativeArray<float4> spheres,
            out NativeArray<int> sphereStart)
        {
            int prepareStage = profiler.BeginMain("コライダー入力準備");

            int fragmentCount = samplingPoints.Count;

            int pointTotal = 0;
            int sphereTotal = 0;

            var pointRange = new NativeArray<int2>(fragmentCount, Allocator.TempJob);
            var settings = new NativeArray<ColliderClusterSettings>(fragmentCount, Allocator.TempJob);
            sphereStart = new NativeArray<int>(fragmentCount, Allocator.Persistent);

            for (int k = 0; k < fragmentCount; k++)
            {
                ColliderClusterSettings setting = fragments[k].ColliderSettings;

                pointRange[k] = new int2(pointTotal, samplingPoints[k].Count);
                settings[k] = setting;
                sphereStart[k] = sphereTotal;

                pointTotal += samplingPoints[k].Count;
                sphereTotal += setting.ClusterCount;
            }

            var points = new NativeArray<float3>(pointTotal, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            for (int k = 0; k < fragmentCount; k++)
            {
                List<Vector3> list = samplingPoints[k];
                int offset = pointRange[k].x;

                for (int p = 0; p < list.Count; p++)
                {
                    points[offset + p] = list[p];
                }
            }

            spheres = new NativeArray<float4>(sphereTotal, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            profiler.EndMain(prepareStage);

            var job = new ColliderClusterJob
            {
                Points = points,
                PointRange = pointRange,
                Settings = settings,
                OutputStart = sphereStart,
                Seed = (uint)UnityEngine.Random.Range(1, int.MaxValue),
                Spheres = spheres
            };

            JobHandle clusterStart = profiler.BeginJob("コライダー計算", default, out int clusterStage);
            JobHandle clusterHandle = profiler.EndJob(clusterStage, job.Schedule(fragmentCount, 1, clusterStart));
            clusterHandle.Complete();
            profiler.Observe(clusterStage);

            points.Dispose();
            pointRange.Dispose();
            settings.Dispose();
        }

        /// <param name="colliderSpheres">ComputeColliderSpheres が求めた全破片の球</param>
        /// <param name="sphereStart">この破片の球の colliderSpheres 上の先頭位置</param>
        /// <param name="original">破片を取得する前に読み取っておいた切断元の値</param>
        /// <param name="fragmentMeshId">
        /// 再切断用にストアへ登録されたメッシュID。登録されていない(＝もう切れない)場合は -1。
        /// </param>
        /// <param name="colliderTicks">コライダーへの反映に掛かった時間(Stopwatchのtick)を加算する</param>
        private void ApplyResult(
            CuttableObject cuttable,
            Mesh mesh,
            NativeArray<float4> colliderSpheres,
            int sphereStart,
            in OriginalSnapshot original,
            int fragmentMeshId,
            ref long colliderTicks)
        {
            GameObject fragObj = cuttable.gameObject;

            // Transform同期
            fragObj.transform.SetPositionAndRotation(original.Position, original.Rotation);
            fragObj.transform.localScale = original.LocalScale;

            // メッシュ設定。この破片が前回の切断で持っていたメッシュはここで破棄される
            cuttable.SetCutMesh(mesh);

            // マテリアルコピー処理
            var fragmentRenderer = cuttable.Renderer;

            if (original.Materials != null && fragmentRenderer != null)
            {
                Material[] originalMaterials = original.Materials;

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

            // アクティブ化で Awake が走り、球コライダーが用意されてから反映する
            long colliderStart = Stopwatch.GetTimestamp();
            cuttable.ApplyColliderSpheres(colliderSpheres, sphereStart);
            colliderTicks += Stopwatch.GetTimestamp() - colliderStart;

            // 切断可否の引き継ぎ。何回でも切断可能なものだけが新しいMeshIdを持つ
            cuttable.InheritCutSettings(original.CanMultiCut);

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
            if (original.HasRig && cuttable.Rig)
            {
                cuttable.Rig.linearVelocity = original.LinearVelocity;
                cuttable.Rig.angularVelocity = original.AngularVelocity;
            }
        }

        /// <summary> 破片への反映で使う、切断元の Transform・マテリアル・切断設定・速度の値 </summary>
        private readonly struct OriginalSnapshot
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 LocalScale;

            /// <summary> 切断元の Renderer の sharedMaterials。Renderer が無ければ null </summary>
            public readonly Material[] Materials;

            public readonly bool CanMultiCut;
            public readonly bool HasRig;
            public readonly Vector3 LinearVelocity;
            public readonly Vector3 AngularVelocity;

            public OriginalSnapshot(CuttableObject original)
            {
                Transform t = original.transform;
                Position = t.position;
                Rotation = t.rotation;
                LocalScale = t.localScale;

                Materials = original.Renderer != null ? original.Renderer.sharedMaterials : null;

                CanMultiCut = original.CanMultiCut;

                HasRig = original.Rig;
                LinearVelocity = HasRig ? original.Rig.linearVelocity : Vector3.zero;
                AngularVelocity = HasRig ? original.Rig.angularVelocity : Vector3.zero;
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

        /// <returns>許容時間を超えたため次のフレームへ送った場合は true</returns>
        private static async UniTask<bool> CheckTime(Stopwatch stopwatch, float limitMs = 5f)
        {
            if (stopwatch.ElapsedMilliseconds <= limitMs) return false;

            await UniTask.Yield();
            stopwatch.Restart();

            return true;
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
