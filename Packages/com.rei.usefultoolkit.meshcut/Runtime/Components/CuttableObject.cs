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

        /// <summary> 初期化の時点で自身に付いていたコライダー。AdoptCutShape で無効にするか、移した形に合わせる </summary>
        private Collider[] _ownColliders;

        /// <summary> _ownColliders の、初期化の時点の有効・無効と形。RestoreInitialShape で戻す先 </summary>
        private OwnColliderShape[] _ownColliderInitialShapes;

        /// <summary> 初期化の時点のメッシュ。RestoreInitialShape で戻す先 </summary>
        private UnityEngine.Mesh _initialMesh;

        /// <summary> 初期化の時点のマテリアル。RestoreInitialShape で戻す先 </summary>
        private Material[] _initialMaterials;

        private bool _initialCanMultiCut;
        private int _initialMaxCutCount;

        private bool _initialized;

        /// <summary> AdoptCutShape でマテリアルを写すときに使い回すバッファ </summary>
        private static readonly List<Material> MaterialBuffer = new();

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// 球コライダーの用意、参照の補完、初期の形の記録を1回だけ行います。
        /// 一度もアクティブになっていないオブジェクトは Awake が走っていないため、非アクティブのまま扱う操作からも呼びます。
        /// </summary>
        internal void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            // 球コライダーを足す前に取り、プレハブ等で元から付いていたものだけを対象にする
            _ownColliders = GetComponents<Collider>();
            _ownColliderInitialShapes = new OwnColliderShape[_ownColliders.Length];
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                _ownColliderInitialShapes[i] = OwnColliderShape.Capture(_ownColliders[i]);
            }

            _colliders = new List<SphereCollider>(_colliderNum);
            AddSphereColliders(_colliderNum);

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

            _initialMesh = Mesh != null ? Mesh.sharedMesh : null;
            _initialMaterials = Renderer != null ? Renderer.sharedMaterials : null;
            _initialCanMultiCut = _canMultiCut;
            _initialMaxCutCount = _maxCutCount;
        }

        private void AddSphereColliders(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var col = gameObject.AddComponent<SphereCollider>();

                col.enabled = false;
                col.sharedMaterial = _physicsMaterial;

                _colliders.Add(col);
            }
        }

        private void DisableOwnColliders()
        {
            foreach (Collider col in _ownColliders)
            {
                if (col != null) col.enabled = false;
            }
        }

        private void RestoreOwnColliders()
        {
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                if (_ownColliders[i] != null) _ownColliderInitialShapes[i].Restore(_ownColliders[i]);
            }
        }

        /// <summary>
        /// 元から持っていたコライダーのうち初期化の時点で有効だったものを、表示中のメッシュの bounds を覆う形に合わせて有効にします。
        /// 合わせられない種類のコライダーと、初期化の時点で無効だったものは無効にします。
        /// </summary>
        /// <returns>1つ以上のコライダーを合わせて有効にしたときは true</returns>
        private bool FitOwnColliders()
        {
            UnityEngine.Mesh mesh = Mesh != null ? Mesh.sharedMesh : null;
            if (mesh == null)
            {
                DisableOwnColliders();
                return false;
            }

            Bounds bounds = mesh.bounds;
            bool fitted = false;

            for (int i = 0; i < _ownColliders.Length; i++)
            {
                Collider col = _ownColliders[i];
                if (col == null) continue;

                if (_ownColliderInitialShapes[i].Enabled && TryFitCollider(col, bounds, mesh))
                {
                    col.enabled = true;
                    fitted = true;
                }
                else
                {
                    col.enabled = false;
                }
            }

            return fitted;
        }

        /// <summary>
        /// コライダーを、ローカル空間の bounds を内側に含む形にします。MeshCollider はメッシュをそのまま使います。
        /// </summary>
        /// <returns>合わせられない種類のコライダーのときは false</returns>
        private static bool TryFitCollider(Collider col, Bounds bounds, UnityEngine.Mesh mesh)
        {
            Vector3 extents = bounds.extents;

            switch (col)
            {
                case BoxCollider box:
                    box.center = bounds.center;
                    box.size = bounds.size;
                    return true;

                case SphereCollider sphere:
                    sphere.center = bounds.center;
                    sphere.radius = extents.magnitude;
                    return true;

                case CapsuleCollider capsule:
                {
                    // 最も長い軸を向きにし、残り2軸の箱の角を半径で覆う。線分の半分の長さを長い軸の extents にすれば箱の全体が入る
                    int direction = extents.x >= extents.y
                        ? (extents.x >= extents.z ? 0 : 2)
                        : (extents.y >= extents.z ? 1 : 2);
                    float a = extents[(direction + 1) % 3];
                    float b = extents[(direction + 2) % 3];
                    float radius = Mathf.Sqrt(a * a + b * b);

                    capsule.direction = direction;
                    capsule.center = bounds.center;
                    capsule.radius = radius;
                    capsule.height = (extents[direction] + radius) * 2f;
                    return true;
                }

                case MeshCollider meshCollider:
                    meshCollider.sharedMesh = mesh;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 切断で生まれた破片の形を自分に移します。
        /// メッシュ(持ち主ごと)、マテリアル、球コライダー、切断設定と回数、切断できるかどうかを移し、自分が元から持っていたコライダーは無効にします。
        /// 移したあとの破片はメッシュの持ち主でなくなり切断もできなくなるので、プールへ返して使い回しても移した形は消えません。
        /// アクティブ・非アクティブは変えません。
        /// </summary>
        public void AdoptCutShape(CuttableObject fragment)
        {
            AdoptCutShape(fragment, AdoptColliderMode.Spheres);
        }

        /// <summary>
        /// 切断で生まれた破片の形を自分に移します。
        /// メッシュ(持ち主ごと)、マテリアル、切断設定と回数、切断できるかどうかを移し、当たり判定は colliderMode のとおりに作ります。
        /// 移したあとの破片はメッシュの持ち主でなくなり切断もできなくなるので、プールへ返して使い回しても移した形は消えません。
        /// アクティブ・非アクティブは変えません。
        /// </summary>
        /// <param name="colliderMode">
        /// Spheres は破片の球コライダーを写し、元から持っていたコライダーを無効にする。
        /// FitOwnColliders は元から持っていたコライダーを移したメッシュの bounds に合わせて使い、球コライダーを無効にする
        /// </param>
        public void AdoptCutShape(CuttableObject fragment, AdoptColliderMode colliderMode)
        {
            if (fragment == null || fragment == this) return;

            EnsureInitialized();
            fragment.EnsureInitialized();

            if (fragment._ownedCutMesh != null)
            {
                SetCutMesh(fragment._ownedCutMesh);
                fragment._ownedCutMesh = null;
            }
            else
            {
                // 破片が持ち主でないメッシュ(共有メッシュ)を表示しているときは、持ち主にならずに表示だけ移す
                UnityEngine.Mesh owned = _ownedCutMesh;
                _ownedCutMesh = null;
                Mesh.sharedMesh = fragment.Mesh.sharedMesh;

                if (owned != null)
                {
                    Destroy(owned);
                }
            }

            if (Renderer != null && fragment.Renderer != null)
            {
                fragment.Renderer.GetSharedMaterials(MaterialBuffer);
                Renderer.SetSharedMaterials(MaterialBuffer);
                MaterialBuffer.Clear();
            }

            if (colliderMode == AdoptColliderMode.FitOwnColliders && FitOwnColliders())
            {
                foreach (SphereCollider col in _colliders)
                {
                    col.enabled = false;
                }
            }
            else
            {
                if (colliderMode == AdoptColliderMode.FitOwnColliders)
                {
                    Debug.LogWarning(
                        $"[UsefulToolkit.MeshCut] {name} には移した形に合わせられるコライダー(初期化の時点で有効な Box / Sphere / Capsule / Mesh Collider)が無いため、破片の球コライダーを写しました。",
                        this);
                }

                CopySphereColliders(fragment);
                DisableOwnColliders();
            }

            _canMultiCut = fragment._canMultiCut;
            _maxCutCount = fragment._maxCutCount;
            _cutCount = fragment._cutCount;

            if (fragment.IsCuttable)
            {
                SetRegisteredMesh(fragment.MeshId);

                if (MeshDataCache.Instance != null)
                {
                    MeshDataCache.Instance.RegisterUser(this);
                }
            }
            else
            {
                DisableCutting();
            }

            fragment.DisableCutting();
        }

        /// <summary> 破片の球コライダーの位置・大きさ・有効無効を自分の球コライダーへ写します </summary>
        private void CopySphereColliders(CuttableObject fragment)
        {
            // 破片はプールのプレハブの設定で球を作るため、数が足りなければ自分の側を増やす
            List<SphereCollider> sourceColliders = fragment._colliders;
            if (sourceColliders.Count > _colliders.Count)
            {
                AddSphereColliders(sourceColliders.Count - _colliders.Count);
            }

            for (int i = 0; i < _colliders.Count; i++)
            {
                SphereCollider col = _colliders[i];

                if (i >= sourceColliders.Count)
                {
                    col.enabled = false;
                    continue;
                }

                SphereCollider source = sourceColliders[i];
                col.center = source.center;
                col.radius = source.radius;
                col.enabled = source.enabled;
            }
        }

        /// <summary>
        /// 初期化の時点のメッシュとマテリアルに戻し、球コライダーを無効に、元から持っていたコライダーを初期の状態(有効・無効と中心や大きさ)に戻します。
        /// 切断設定は初期の値に、切断回数は0に戻ります。持ち主になっていた切断後のメッシュは破棄し、共有メッシュは破棄しません。
        /// 戻したあとは切断できない状態になるので、切断できるようにするには MeshDataCache.Register で登録し直してください。
        /// アクティブ・非アクティブは変えません。
        /// </summary>
        public void RestoreInitialShape()
        {
            EnsureInitialized();

            UnityEngine.Mesh owned = _ownedCutMesh;
            _ownedCutMesh = null;

            if (Mesh != null)
            {
                Mesh.sharedMesh = _initialMesh;
            }

            if (owned != null)
            {
                Destroy(owned);
            }

            if (Renderer != null && _initialMaterials != null)
            {
                Renderer.sharedMaterials = _initialMaterials;
            }

            foreach (SphereCollider col in _colliders)
            {
                col.enabled = false;
            }

            RestoreOwnColliders();

            _canMultiCut = _initialCanMultiCut;
            _maxCutCount = _initialMaxCutCount;
            _cutCount = 0;

            DisableCutting();
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
        /// 半径が負(ColliderClusterJob.Disabled)の球に対応するコライダーと、AdoptCutShape で ClusterCount を超えて増えたコライダーは無効にします。
        /// </summary>
        public void ApplyColliderSpheres(NativeArray<float4> spheres, int start)
        {
            for (int i = 0; i < _colliders.Count; i++)
            {
                SphereCollider col = _colliders[i];

                if (i >= _colliderNum)
                {
                    col.enabled = false;
                    continue;
                }

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

        /// <summary> 元から持っていたコライダー1つぶんの、有効・無効と形。種類ごとに使う値だけを持つ </summary>
        private readonly struct OwnColliderShape
        {
            public readonly bool Enabled;
            public readonly Vector3 Center;
            public readonly Vector3 Size;
            public readonly float Radius;
            public readonly float Height;
            public readonly int Direction;
            public readonly UnityEngine.Mesh SharedMesh;

            private OwnColliderShape(bool enabled, Vector3 center, Vector3 size, float radius, float height, int direction,
                UnityEngine.Mesh sharedMesh)
            {
                Enabled = enabled;
                Center = center;
                Size = size;
                Radius = radius;
                Height = height;
                Direction = direction;
                SharedMesh = sharedMesh;
            }

            public static OwnColliderShape Capture(Collider col)
            {
                return col switch
                {
                    BoxCollider box => new OwnColliderShape(box.enabled, box.center, box.size, 0f, 0f, 0, null),
                    SphereCollider sphere => new OwnColliderShape(sphere.enabled, sphere.center, Vector3.zero, sphere.radius, 0f, 0, null),
                    CapsuleCollider capsule => new OwnColliderShape(capsule.enabled, capsule.center, Vector3.zero, capsule.radius,
                        capsule.height, capsule.direction, null),
                    MeshCollider meshCollider => new OwnColliderShape(meshCollider.enabled, Vector3.zero, Vector3.zero, 0f, 0f, 0,
                        meshCollider.sharedMesh),
                    _ => new OwnColliderShape(col.enabled, Vector3.zero, Vector3.zero, 0f, 0f, 0, null)
                };
            }

            public void Restore(Collider col)
            {
                switch (col)
                {
                    case BoxCollider box:
                        box.center = Center;
                        box.size = Size;
                        break;

                    case SphereCollider sphere:
                        sphere.center = Center;
                        sphere.radius = Radius;
                        break;

                    case CapsuleCollider capsule:
                        capsule.direction = Direction;
                        capsule.center = Center;
                        capsule.radius = Radius;
                        capsule.height = Height;
                        break;

                    case MeshCollider meshCollider:
                        meshCollider.sharedMesh = SharedMesh;
                        break;
                }

                col.enabled = Enabled;
            }
        }
    }
}