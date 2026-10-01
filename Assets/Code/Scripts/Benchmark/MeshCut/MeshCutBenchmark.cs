using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UsefulToolkit.MeshCut;
using Object = UnityEngine.Object;

namespace Sandbox.Benchmark.MeshCut
{
    /// <summary>
    /// MeshCut の切断〜破片反映までを、切断対象の三角形数とオブジェクト数の組み合わせごとに繰り返し計測し、
    /// 組み合わせごとの中央値を MeshCutProfile の表として Console へ出力する。
    /// 空のシーンの GameObject に付けて再生すると、MeshDataCache / FragmentPool / CutBlade と
    /// 切断対象(UV球)を実行時に生成して計測する。
    /// </summary>
    public class MeshCutBenchmark : MonoBehaviour
    {
        [SerializeField, Tooltip("再生開始時に自動で実行する")]
        private bool _runOnStart = true;

        [SerializeField, Tooltip("切断対象1つあたりの三角形数の目安")]
        private int[] _triangleCounts = { 1000, 10000, 100000 };

        [SerializeField, Tooltip("1回の切断で同時に切る対象の数")]
        private int[] _objectCounts = { 1, 10, 50 };

        [SerializeField, Min(0), Tooltip("集計に含めない事前実行の回数")]
        private int _warmupCount = 1;

        [SerializeField, Min(1), Tooltip("集計する実行回数")]
        private int _repeatCount = 5;

        [SerializeField, Min(1), Tooltip("1回の切断で扱う三角形数の合計がこれを超える組み合わせはスキップする")]
        private int _maxTotalTriangles = 5000000;

        [SerializeField, Tooltip("集計した中央値に加えて、各回の結果も出力する")]
        private bool _logEachRun;

        [SerializeField, Tooltip("切断対象と破片に使うマテリアル。未設定でも計測はできる")]
        private Material _material;

        private const float ObjectSpacing = 2.5f;

        private MultiCutBlade _blade;
        private MeshCutObjectPool _pool;
        private GameObject _fragmentTemplate;

        /// <summary>
        /// 計測中の切断元メッシュ。Resources.UnloadUnusedAssets で破棄されないよう、
        /// シーン上のコンポーネントのフィールドから参照しておく。
        /// </summary>
        private Mesh _sourceMesh;

        private bool _running;

        private async void Start()
        {
            if (_runOnStart)
            {
                await RunAsync();
            }
        }

        [ContextMenu("ベンチマーク実行")]
        private void RunFromContextMenu()
        {
            RunAsync().Forget();
        }

        /// <summary> 全ての組み合わせを計測します。 </summary>
        public async UniTask RunAsync()
        {
            if (_running)
            {
                Debug.LogWarning("[MeshCutBenchmark] 既に実行中です。");
                return;
            }

            _running = true;

            try
            {
                await SetupSystemAsync();

                string environment = DescribeEnvironment();
                Debug.Log($"[MeshCutBenchmark] 計測開始\n{environment}");

                foreach (int triangleCount in _triangleCounts)
                {
                    _sourceMesh = CreateSphere(triangleCount);

                    foreach (int objectCount in _objectCounts)
                    {
                        await RunCaseAsync(objectCount, environment);
                    }

                    Destroy(_sourceMesh);
                    _sourceMesh = null;
                }

                Debug.Log("[MeshCutBenchmark] 計測終了");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _running = false;
            }
        }

        private async UniTask RunCaseAsync(int objectCount, string environment)
        {
            int triangleCount = CountTriangles(_sourceMesh);
            string caseName = $"三角形 {triangleCount:N0} × {objectCount} 個";

            long totalTriangles = (long)triangleCount * objectCount;
            if (totalTriangles > _maxTotalTriangles)
            {
                Debug.LogWarning(
                    $"[MeshCutBenchmark] {caseName}: 三角形数の合計 {totalTriangles:N0} が " +
                    $"上限 {_maxTotalTriangles:N0} を超えるためスキップしました。");
                return;
            }

            var profiles = new List<MeshCutProfile>(_repeatCount);

            for (int run = 0; run < _warmupCount + _repeatCount; run++)
            {
                CuttableObject[] targets = SpawnTargets(objectCount);
                MeshDataCache.Instance.Initialize();

                MeshCutProfile before = _blade.LastProfile;
                await _blade.ExecuteCut(targets);
                MeshCutProfile profile = _blade.LastProfile;

                await CleanupAsync(targets);

                if (profile == null || ReferenceEquals(profile, before))
                {
                    Debug.LogError($"[MeshCutBenchmark] {caseName}: 切断が完了しなかったため、この組み合わせを中断しました。");
                    return;
                }

                bool isWarmup = run < _warmupCount;
                if (isWarmup) continue;

                profiles.Add(profile);

                if (_logEachRun)
                {
                    Debug.Log($"[MeshCutBenchmark] {caseName} ({run - _warmupCount + 1}/{_repeatCount}回目)\n{profile}");
                }
            }

            MeshCutProfile median = MeshCutProfile.Median(profiles, $"{caseName} の中央値 ({profiles.Count} 回)");
            Debug.Log($"{median}{environment}");
        }

        // ── シーン構築 ──

        private async UniTask SetupSystemAsync()
        {
            if (MeshDataCache.Instance == null)
            {
                var cacheObject = new GameObject("MeshDataCache");
                cacheObject.transform.SetParent(transform, false);
                cacheObject.AddComponent<MeshDataCache>();

                // MeshDataCache.Instance は Start で設定される
                await UniTask.WaitUntil(() => MeshDataCache.Instance != null);
            }
            else
            {
                Debug.LogWarning("[MeshCutBenchmark] シーン上の既存の MeshDataCache を使います。その配下の切断対象も毎回登録し直されます。");
            }

            if (_pool == null)
            {
                _fragmentTemplate = CreateFragmentTemplate();

                int capacity = 2;
                foreach (int count in _objectCounts)
                {
                    capacity = Mathf.Max(capacity, count * 2);
                }

                var poolObject = new GameObject("FragmentPool");
                poolObject.transform.SetParent(transform, false);
                _pool = poolObject.AddComponent<MeshCutObjectPool>();

                // 生成は Start で行われるため、同じフレーム内に設定すれば間に合う
                SetSerializedField(_pool, "_prefab", _fragmentTemplate);
                SetSerializedField(_pool, "_generateCapacity", capacity);
            }

            if (_blade == null)
            {
                var bladeObject = new GameObject("CutBlade");
                bladeObject.transform.SetParent(transform, false);

                // 軸に揃った平面だと頂点が平面上に乗りやすいので、少し傾けて中心から外す
                bladeObject.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.Euler(3f, 0f, 2f));

                _blade = bladeObject.AddComponent<MultiCutBlade>();
                SetSerializedField(_blade, "_pool", _pool);
                _blade.CollectProfile = true;
            }

            await _pool.WaitForGeneration();
        }

        private GameObject CreateFragmentTemplate()
        {
            // 非アクティブのまま複製元にする。破片は反映時にアクティブ化されたときに Awake が走る
            var template = new GameObject("FragmentTemplate");
            template.SetActive(false);
            template.transform.SetParent(transform, false);

            var filter = template.AddComponent<MeshFilter>();
            var meshRenderer = template.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;

            var cuttable = template.AddComponent<CuttableObject>();
            cuttable.Mesh = filter;
            cuttable.Renderer = meshRenderer;
            cuttable.CapMaterial = _material;

            return template;
        }

        private CuttableObject[] SpawnTargets(int objectCount)
        {
            Transform cacheRoot = MeshDataCache.Instance.transform;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(objectCount));
            float offset = (columns - 1) * ObjectSpacing * 0.5f;

            var targets = new CuttableObject[objectCount];

            for (int i = 0; i < objectCount; i++)
            {
                var target = new GameObject($"BenchTarget_{i}");
                target.transform.SetParent(cacheRoot, false);
                target.transform.position = new Vector3(
                    i % columns * ObjectSpacing - offset,
                    0f,
                    i / columns * ObjectSpacing - offset);

                var filter = target.AddComponent<MeshFilter>();
                filter.sharedMesh = _sourceMesh;

                var meshRenderer = target.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _material;

                var cuttable = target.AddComponent<CuttableObject>();
                cuttable.Mesh = filter;
                cuttable.Renderer = meshRenderer;
                cuttable.CapMaterial = _material;

                targets[i] = cuttable;
            }

            return targets;
        }

        /// <summary>
        /// 切断元を破棄し、破片を非アクティブに戻し、切断で生成されたメッシュのうち参照されなくなったものを解放します。
        /// </summary>
        private async UniTask CleanupAsync(CuttableObject[] targets)
        {
            foreach (CuttableObject target in targets)
            {
                if (target != null)
                {
                    Destroy(target.gameObject);
                }
            }

            foreach (CuttableObject fragment in _pool.GetComponentsInChildren<CuttableObject>())
            {
                fragment.gameObject.SetActive(false);
            }

            await Resources.UnloadUnusedAssets();
            GC.Collect();

            await UniTask.Yield();
        }

        /// <summary>
        /// パッケージ側に実行時に設定するAPIが無いため、セットアップウィンドウが SerializedObject で設定しているのと
        /// 同じ private フィールドをリフレクションで設定します。フィールド名が変わると例外になります。
        /// </summary>
        private static void SetSerializedField(Object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }

            field.SetValue(target, value);
        }

        // ── メッシュ生成 ──

        /// <summary> 三角形数がおよそ triangleCount になる、半径1のUV球を生成します。 </summary>
        private static Mesh CreateSphere(int triangleCount)
        {
            // 経線 n 本・緯線 n/2 本で三角形数はおよそ n^2 になる
            int longitude = Mathf.Max(8, Mathf.RoundToInt(Mathf.Sqrt(triangleCount)) & ~1);
            int latitude = longitude / 2;

            var vertices = new List<Vector3>((longitude + 1) * (latitude + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);

            for (int lat = 0; lat <= latitude; lat++)
            {
                float theta = Mathf.PI * lat / latitude;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= longitude; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / longitude;
                    var normal = new Vector3(sinTheta * Mathf.Cos(phi), cosTheta, sinTheta * Mathf.Sin(phi));

                    vertices.Add(normal);
                    normals.Add(normal);
                    uvs.Add(new Vector2((float)lon / longitude, (float)lat / latitude));
                }
            }

            var indices = new List<int>(longitude * latitude * 6);
            int stride = longitude + 1;

            for (int lat = 0; lat < latitude; lat++)
            {
                for (int lon = 0; lon < longitude; lon++)
                {
                    int a = lat * stride + lon;
                    int b = a + stride;
                    int c = a + 1;
                    int d = b + 1;

                    // 極の行は四角形の片側が潰れるので、潰れない側の三角形だけを作る
                    if (lat != 0)
                    {
                        indices.Add(a);
                        indices.Add(c);
                        indices.Add(b);
                    }

                    if (lat != latitude - 1)
                    {
                        indices.Add(c);
                        indices.Add(d);
                        indices.Add(b);
                    }
                }
            }

            var mesh = new Mesh
            {
                name = $"BenchSphere_{indices.Count / 3}",
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static int CountTriangles(Mesh mesh)
        {
            long indexCount = 0;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                indexCount += mesh.GetIndexCount(s);
            }

            return (int)(indexCount / 3);
        }

        private static string DescribeEnvironment()
        {
            return
                $"環境: {(Application.isEditor ? "Editor" : Debug.isDebugBuild ? "Development Build" : "Release Build")}" +
                $" / Burst {(BurstCompiler.IsEnabled ? "有効" : "無効")}" +
                $" / Burst Safety Checks {(BurstCompiler.Options.EnableBurstSafetyChecks ? "有効" : "無効")}" +
                $" / Job Debugger {(JobsUtility.JobDebuggerEnabled ? "有効" : "無効")}" +
                $" / ワーカー {JobsUtility.JobWorkerCount}" +
                $" / vSync {QualitySettings.vSyncCount} / targetFrameRate {Application.targetFrameRate}";
        }
    }
}
