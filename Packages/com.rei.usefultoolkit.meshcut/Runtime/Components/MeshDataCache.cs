using System.Collections.Generic;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 配下のCuttableObjectが参照するメッシュをユニーク登録し、各CuttableObjectにMeshIdを割り振る。
    /// 実データはNativeMeshDataStoreとしてNativeArrayにフラット化して保持するため、Jobから直接読める。
    /// Start の時点でアクティブな子だけが自動で登録される。実行中に足すものや非アクティブで待たせるものは Register で登録すること。
    ///
    /// 何回でも切断可能なオブジェクトの破片は実行時に追加登録されるため、ストアは切断のたびに伸びる。
    /// 一定量を超えたら、生存しているCuttableObjectが参照するメッシュだけを残して自動的に再構築する。
    ///
    /// 切断のJobはストアのデータを複製せずに直接読む。ストアの NativeList はリサイズや破棄で中身の位置が変わるため、
    /// ストアを変更・破棄する処理は必ず CompleteStoreReaders で読み取り中のJobを完了させてから行うこと。
    /// </summary>
    public class MeshDataCache : MonoBehaviour
    {
        public static MeshDataCache Instance { get; private set; }

        public NativeMeshDataStore Store { get; private set; }

        [SerializeField, Min(0), Tooltip("初期登録ぶんに対してこの頂点数を超えて追加されたら、ストアを再構築する")]
        private int _rebuildVertexThreshold = 200000;

        /// <summary> MeshIdを持っているCuttableObject。ストア再構築時にIDを振り直す対象。 </summary>
        private readonly HashSet<CuttableObject> _users = new();

        /// <summary> ストアへ登録済みの共有メッシュ(プレハブのメッシュ等)と、そのメッシュID </summary>
        private Dictionary<Mesh, int> _sourceMeshIds = new();

        /// <summary> 再構築の要否を判断するための基準頂点数 </summary>
        private int _baselineVertexCount;

        /// <summary> ストアを読んでいるJobのハンドル(AddStoreReader で登録されたもの全て) </summary>
        private JobHandle _storeReaders;

        /// <summary>
        /// ストアを読むJobのハンドルを登録します。ストアを変更・破棄する前に、登録されたJobの完了を待つために使います。
        /// </summary>
        public void AddStoreReader(JobHandle handle)
        {
            _storeReaders = _storeReaders.IsCompleted ? handle : JobHandle.CombineDependencies(_storeReaders, handle);
        }

        /// <summary>
        /// ストアを読んでいるJobを全て完了させます。ストアへの追加登録・再構築・破棄の前に呼んでください。
        /// </summary>
        public void CompleteStoreReaders()
        {
            _storeReaders.Complete();
            _storeReaders = default;
        }

        private void Start()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Initialize();
        }

        public void Initialize()
        {
            CompleteStoreReaders();
            Store?.Dispose();
            Store = new NativeMeshDataStore();
            _users.Clear();
            _sourceMeshIds.Clear();

            var objects = GetComponentsInChildren<CuttableObject>();

            foreach (var cuttable in objects)
            {
                var mesh = cuttable.Mesh.sharedMesh;
                if (mesh == null) continue;

                if (!_sourceMeshIds.TryGetValue(mesh, out int meshId))
                {
                    meshId = Store.Add(mesh);
                    _sourceMeshIds.Add(mesh, meshId);
                }

                cuttable.SetRegisteredMesh(meshId);
                _users.Add(cuttable);
            }

            _baselineVertexCount = Store.Vertices.Length;

            Debug.Log($"[UsefulToolkit.MeshCut] Cache Completed. Cache Count: {Store.MeshCount}");
        }

        /// <summary>
        /// 実行中に1つの CuttableObject を登録し、切断できる状態にします。
        /// 非アクティブなものや、このコンポーネントの子でないものも登録できます。
        /// 共有メッシュ(プレハブのメッシュ等)はストアへ1回だけ追加し、2回目以降は同じメッシュIDを使います。
        /// </summary>
        /// <returns>ストアがまだ無い、または表示中のメッシュが無いときは false</returns>
        public bool Register(CuttableObject cuttable)
        {
            if (cuttable == null) return false;

            if (Store == null)
            {
                Debug.LogError(
                    $"[UsefulToolkit.MeshCut] ストアがまだ作られていないため {cuttable.name} を登録できません。MeshDataCache の Start の後で登録してください。");
                return false;
            }

            cuttable.EnsureInitialized();

            Mesh mesh = cuttable.Mesh != null ? cuttable.Mesh.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError($"[UsefulToolkit.MeshCut] {cuttable.name} に表示中のメッシュが無いため登録できません。");
                return false;
            }

            int meshId;

            if (cuttable.ShowsOwnedCutMesh)
            {
                // 切断で生成したメッシュは断面サブメッシュが最後にある。次の切断で断面を増やさずそこへ追記させる
                CompleteStoreReaders();
                meshId = Store.Add(mesh, mesh.subMeshCount - 1);
            }
            else if (!_sourceMeshIds.TryGetValue(mesh, out meshId))
            {
                CompleteStoreReaders();
                meshId = Store.Add(mesh);
                _sourceMeshIds.Add(mesh, meshId);
            }

            cuttable.SetRegisteredMesh(meshId);
            _users.Add(cuttable);
            return true;
        }

        /// <summary>
        /// 実行時に追加登録されたメッシュを持つCuttableObjectを、ストア再構築の対象として記録します。
        /// </summary>
        public void RegisterUser(CuttableObject cuttable)
        {
            if (cuttable == null) return;

            _users.Add(cuttable);
        }

        /// <summary> 指定メッシュIDの頂点範囲・三角形範囲・サブメッシュ数を取得する </summary>
        public bool TryGet(int meshId, out int2 vertexRange, out int2 triangleRange, out int submeshCount)
        {
            if (Store == null || meshId < 0 || meshId >= Store.MeshCount)
            {
                Debug.LogError($"[UsefulToolkit.MeshCut] IDの値が不正です {meshId}");
                vertexRange = default;
                triangleRange = default;
                submeshCount = 0;
                return false;
            }

            vertexRange = Store.MeshVertexRange[meshId];
            triangleRange = Store.MeshTriangleRange[meshId];
            submeshCount = Store.MeshSubmeshCount[meshId];
            return true;
        }

        /// <summary>
        /// 追加登録によってストアが膨らんでいれば再構築します。
        /// 再構築する場合は、ストアを読んでいるJobの完了を待ってから行います。
        /// </summary>
        public void RebuildIfNeeded()
        {
            if (Store == null) return;
            if (Store.Vertices.Length - _baselineVertexCount <= _rebuildVertexThreshold) return;

            Rebuild();
        }

        /// <summary>
        /// 生存していて、かつ切断可能なCuttableObjectが参照しているメッシュだけを残してストアを作り直します。
        /// 切断済みのオブジェクトが参照していたエントリはここで破棄されます。
        /// </summary>
        public void Rebuild()
        {
            if (Store == null) return;

            CompleteStoreReaders();

            var newStore = new NativeMeshDataStore();
            var idMap = new Dictionary<int, int>();

            // 破棄済みのオブジェクトと、もう切断されないオブジェクトを対象から外す
            _users.RemoveWhere(user => user == null || !user.IsCuttable);

            foreach (CuttableObject user in _users)
            {
                if (idMap.ContainsKey(user.MeshId)) continue;

                idMap.Add(user.MeshId, newStore.CopyMeshFrom(Store, user.MeshId));
            }

            foreach (CuttableObject user in _users)
            {
                user.SetRegisteredMesh(idMap[user.MeshId]);
            }

            // 使う者が残っている共有メッシュだけ、新しいIDで覚え直す
            var sourceMeshIds = new Dictionary<Mesh, int>(_sourceMeshIds.Count);
            foreach (KeyValuePair<Mesh, int> pair in _sourceMeshIds)
            {
                if (idMap.TryGetValue(pair.Value, out int newId))
                {
                    sourceMeshIds.Add(pair.Key, newId);
                }
            }

            _sourceMeshIds = sourceMeshIds;

            int before = Store.MeshCount;

            Store.Dispose();
            Store = newStore;

            _baselineVertexCount = Store.Vertices.Length;

            Debug.Log($"[UsefulToolkit.MeshCut] メッシュストアを再構築しました。{before} → {Store.MeshCount} メッシュ");
        }

        public void Unload()
        {
            CompleteStoreReaders();
            Store?.Dispose();
            Store = null;
            _users.Clear();
            _sourceMeshIds.Clear();
            _baselineVertexCount = 0;
            Debug.Log("[UsefulToolkit.MeshCut] キャッシュを解放しました。");
        }

        private void OnDestroy()
        {
            CompleteStoreReaders();
            Store?.Dispose();

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
