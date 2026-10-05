using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UsefulToolkit.Utility;
using Random = UnityEngine.Random;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 切断可能オブジェクト。破片としても使い回される。
    /// コライダーはサンプリング点のk-meansクラスタリング結果(ColliderClusterJob)から球コライダーで近似する。
    /// </summary>
    public class CuttableObject : MonoBehaviour, IRecyclable
    {
        public int RecycleId { get; set; }
        public int MeshId { get; set; }

        public bool IsCuttable { get; private set; }

        public bool CanMultiCut => _canMultiCut;

        /// <summary> 部位の系統(この部位と、そこから生まれた破片)ごとの切断回数の上限。0は上限なし </summary>
        public int MaxCutCount => _maxCutCount;

        /// <summary> この部位の系統が、これまでに切断された回数 </summary>
        public int CutCount => _cutCount;

        /// <summary>
        /// このオブジェクトを切断して生まれる破片が、もう一度切断できるか。
        /// CanMultiCut が有効で、破片が引き継ぐ回数(CutCount + 1)が上限に達しないときに true。上限は CanMultiCut が有効なときだけ効く。
        /// </summary>
        internal bool CanCutFragments => _canMultiCut && (_maxCutCount <= 0 || _cutCount + 1 < _maxCutCount);

        public Rigidbody Rig;
        public Renderer Renderer;

        public void OnRecycle()
        {
            ReuseAction?.Invoke();
            gameObject.SetActive(false);
            IsCuttable = true;
        }

        public Action ReuseAction;

        public Material CapMaterial;
        public MeshFilter Mesh;

        /// <summary>
        /// 切断で生成され、この破片が持ち主になっているメッシュ。
        /// 別のメッシュへ差し替えるときと、この破片が破棄されるときに Destroy する。
        /// 切断で生成されたものだけを入れること(プレハブ等の共有アセットを入れると、それごと破棄してしまう)。
        /// </summary>
        private UnityEngine.Mesh _ownedCutMesh;

        /// <summary>
        /// 切断で生成されたメッシュを表示し、この破片を持ち主にします。
        /// 以前に持ち主になっていたメッシュは、参照する者がいなくなるため破棄します。
        /// </summary>
        public void SetCutMesh(UnityEngine.Mesh mesh)
        {
            if (_ownedCutMesh != null && _ownedCutMesh != mesh)
            {
                Destroy(_ownedCutMesh);
            }

            _ownedCutMesh = mesh;
            Mesh.sharedMesh = mesh;
        }

        /// <summary> 表示中のメッシュが、切断で生成されこのオブジェクトが持ち主になっているものか </summary>
        internal bool ShowsOwnedCutMesh => _ownedCutMesh != null && Mesh != null && Mesh.sharedMesh == _ownedCutMesh;

        private void OnDestroy()
        {
            if (_ownedCutMesh != null)
            {
                Destroy(_ownedCutMesh);
            }
        }

        /// <summary>
        /// NativeMeshDataStore に登録されたメッシュIDを設定し、切断可能な状態にします。
        /// MeshDataCache への登録と、もう一度切断できる破片への引き継ぎで使います。
        /// </summary>
        public void SetRegisteredMesh(int meshId)
        {
            MeshId = meshId;
            IsCuttable = true;
        }

        /// <summary>
        /// これ以上切断できない状態にします。
        /// 切断済みの元オブジェクトと、もう一度は切断できない破片に対して使います。
        /// </summary>
        public void DisableCutting()
        {
            IsCuttable = false;
        }

        /// <summary>
        /// 切断元から切断に関する設定(CanMultiCut・切断回数の上限)を引き継ぎます。
        /// 切断回数は切断元の回数 + 1 になります。
        /// </summary>
        public void InheritCutSettings(CuttableObject source)
        {
            if (source == null) return;

            InheritCutSettings(source._canMultiCut, source._maxCutCount, source._cutCount);
        }

        /// <summary> 切断元から読み取っておいた設定を引き継ぎます。切断回数は sourceCutCount + 1 になります。 </summary>
        internal void InheritCutSettings(bool canMultiCut, int maxCutCount, int sourceCutCount)
        {
            _canMultiCut = canMultiCut;
            _maxCutCount = maxCutCount;
            _cutCount = sourceCutCount + 1;
        }

        [SerializeField, Tooltip("複数回の切断を許可するか")]
        private bool _canMultiCut;

        [SerializeField, Min(0), Tooltip("部位の系統(この部位と、そこから生まれた破片)を切断できる回数の上限。0は上限なし。Can Multi Cut が有効なときだけ効く")]
        private int _maxCutCount;

        private int _cutCount;

        [SerializeField] private PhysicsMaterial _physicsMaterial;

        // ColliderClusterJobが軸方向の固定6点を必ず追加するため、7未満だとクラスタ中心が不足して破綻する
        [SerializeField, Min(7), Tooltip("破片に生成する球コライダーの数(7以上)")]
        private int _colliderNum = 10;

        [Header("Collider設定")] [SerializeField, Range(0.5f, 1f), Tooltip("基本縮小率")]
        private float _baseShrink = 0.95f;

        [SerializeField, Range(0.5f, 1f), Tooltip("低密度なクラスタの差異の最小縮小率")]
        private float _densityShrinkMin = 0.85f;

        [SerializeField, Min(1), Tooltip("密度閾値")]
        private int _densityThreshold = 10;

        [SerializeField, Min(0f), Tooltip("最大半径制限")]
        private float _maxRadius = 0.5f;


        private List<SphereCollider> _colliders;

        private bool _initialized;

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 球コライダーの用意と参照の補完を1回だけ行います。
        /// 一度もアクティブになっていないオブジェクトは Awake が走っていないため、非アクティブのまま扱う操作からも呼びます。
        /// </summary>
        internal void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _colliders = new List<SphereCollider>(_colliderNum);

            for (int i = 0; i < _colliderNum; i++)
            {
                var col = gameObject.AddComponent<SphereCollider>();

                col.enabled = false;
                col.sharedMaterial = _physicsMaterial;

                _colliders.Add(col);
            }

            if (Mesh == null)
            {
                TryGetComponent(out Mesh);
            }

            if (Rig == null)
            {
                TryGetComponent(out Rig);
            }

            if (Renderer == null)
            {
                TryGetComponent(out Renderer);
            }
        }

        /// <summary> この破片の球コライダーを ColliderClusterJob で求めるときの設定値 </summary>
        public ColliderClusterSettings ColliderSettings => new()
        {
            ClusterCount = _colliderNum,
            BaseShrink = _baseShrink,
            DensityShrinkMin = _densityShrinkMin,
            DensityThreshold = _densityThreshold,
            MaxRadius = _maxRadius
        };

        /// <summary>
        /// 切断結果のサンプリング点から球コライダーを配置します。
        /// 内部で ColliderClusterJob をこの破片1つぶんだけメインスレッドで実行します。
        /// 複数の破片をまとめて処理する場合は、ColliderClusterJob を直接スケジュールして ApplyColliderSpheres で反映してください。
        /// </summary>
        /// <param name="samplingPoints">
        /// MultiMeshCut が出力するサンプリング点。切断は元オブジェクトのローカル空間で行われるため、
        /// これらは既に「メッシュローカル空間」の座標です。破片のTransformは切断元と同一に設定されるので、
        /// SphereCollider.center が期待する自身のローカル座標としてそのまま使えます。
        /// </param>
        public void SetupCollider(List<Vector3> samplingPoints)
        {
            int sampleCount = samplingPoints.Count;

            var points = new NativeArray<float3>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < sampleCount; i++)
            {
                points[i] = samplingPoints[i];
            }

            var pointRange = new NativeArray<int2>(1, Allocator.TempJob);
            pointRange[0] = new int2(0, sampleCount);

            var settings = new NativeArray<ColliderClusterSettings>(1, Allocator.TempJob);
            settings[0] = ColliderSettings;

            var outputStart = new NativeArray<int>(1, Allocator.TempJob);
            var spheres = new NativeArray<float4>(_colliderNum, Allocator.TempJob);

            new ColliderClusterJob
            {
                Points = points,
                PointRange = pointRange,
                Settings = settings,
                OutputStart = outputStart,
                Seed = (uint)Random.Range(1, int.MaxValue),
                Spheres = spheres
            }.Run(1);

            ApplyColliderSpheres(spheres, 0);

            points.Dispose();
            pointRange.Dispose();
            settings.Dispose();
            outputStart.Dispose();
            spheres.Dispose();
        }

        /// <summary>
        /// ColliderClusterJob が求めた球を球コライダーへ反映します。
        /// spheres[start] から ColliderSettings.ClusterCount 個を、自身の球コライダーに順に割り当てます。
        /// 半径が負(ColliderClusterJob.Disabled)の球に対応するコライダーは無効にします。
        /// </summary>
        public void ApplyColliderSpheres(NativeArray<float4> spheres, int start)
        {
            for (int i = 0; i < _colliders.Count; i++)
            {
                SphereCollider col = _colliders[i];
                float4 sphere = spheres[start + i];

                if (sphere.w < 0f)
                {
                    col.enabled = false;
                    continue;
                }

                col.enabled = true;
                col.center = sphere.xyz;
                col.radius = sphere.w;
            }
        }
    }
}