using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.Utility;
using Random = UnityEngine.Random;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 切断可能オブジェクト。破片としても使い回される。
    /// コライダーはサンプリング点のk-meansクラスタリング結果から球コライダーで近似する(この処理のみメインスレッド)。
    /// </summary>
    public class CuttableObject : MonoBehaviour, IRecyclable
    {
        public int RecycleId { get; set; }
        public int MeshId { get; set; }

        public bool IsCuttable { get; private set; }

        public bool CanMultiCut => _canMultiCut;

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
        /// NativeMeshDataStore に登録されたメッシュIDを設定し、切断可能な状態にします。
        /// MeshDataCache の初期登録と、何回でも切断可能なオブジェクトの破片への引き継ぎで使います。
        /// </summary>
        public void SetRegisteredMesh(int meshId)
        {
            MeshId = meshId;
            IsCuttable = true;
        }

        /// <summary>
        /// これ以上切断できない状態にします。
        /// 1回だけ切断可能なオブジェクトから生まれた破片に対して使います。
        /// </summary>
        public void DisableCutting()
        {
            IsCuttable = false;
        }

        /// <summary> 切断元から切断に関する設定を引き継ぎます。 </summary>
        public void InheritCutSettings(CuttableObject source)
        {
            if (source == null) return;

            _canMultiCut = source._canMultiCut;
        }

        [SerializeField, Tooltip("複数回の切断を許可するか")]
        private bool _canMultiCut;

        [SerializeField] private PhysicsMaterial _physicsMaterial;

        // ClusteringVertsが軸方向の固定6点を必ず追加するため、7未満だとクラスタ中心が不足して破綻する
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

        private void Awake()
        {
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

        /// <summary>
        /// 切断結果のサンプリング点から球コライダーを配置します。
        /// </summary>
        /// <param name="samplingPoints">
        /// MultiMeshCut が出力するサンプリング点。切断は元オブジェクトのローカル空間で行われるため、
        /// これらは既に「メッシュローカル空間」の座標です。破片のTransformは切断元と同一に設定されるので、
        /// SphereCollider.center が期待する自身のローカル座標としてそのまま使えます。
        /// </param>
        public void SetupCollider(List<Vector3> samplingPoints)
        {
            int sampleCount = samplingPoints.Count;

            if (sampleCount == 0)
            {
                DisableUnusedColliders(0);
                return;
            }

            // クラスタリング
            List<Vector3> centers = ClusteringVerts(samplingPoints);

            int clusterCount = centers.Count;

            int[] belongCluster = new int[sampleCount];
            int[] clusterVertCount = new int[clusterCount];

            // 所属クラスタ探索
            for (int i = 0; i < sampleCount; i++)
            {
                float minDist = float.MaxValue;
                int nearest = 0;

                for (int j = 0; j < clusterCount; j++)
                {
                    float dist = (centers[j] - samplingPoints[i]).sqrMagnitude;

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = j;
                    }
                }

                belongCluster[i] = nearest;
                clusterVertCount[nearest]++;
            }

            // Collider設定
            for (int i = 0; i < clusterCount; i++)
            {
                SphereCollider col = _colliders[i];

                // 1点も所属しなかったクラスタは中心が初期のランダム位置のまま残っており、
                // 半径0のコライダーがメッシュと無関係な場所に置かれてしまうため無効化する
                if (clusterVertCount[i] == 0)
                {
                    col.enabled = false;
                    continue;
                }

                float maxDistSq = 0f;

                for (int v = 0; v < sampleCount; v++)
                {
                    if (belongCluster[v] != i)
                        continue;

                    float distSq =
                        (centers[i] - samplingPoints[v]).sqrMagnitude;

                    if (distSq > maxDistSq)
                        maxDistSq = distSq;
                }

                float radius = Mathf.Sqrt(maxDistSq);

                radius *= _baseShrink;

                if (clusterVertCount[i] < _densityThreshold)
                {
                    float t =
                        1f - (clusterVertCount[i] / (float)_densityThreshold);

                    float densityShrink =
                        Mathf.Lerp(_baseShrink, _densityShrinkMin, t);

                    radius *= densityShrink;
                }

                radius = Mathf.Min(radius, _maxRadius);

                col.enabled = true;
                col.center = centers[i];
                col.radius = radius;
            }

            DisableUnusedColliders(clusterCount);
        }

        private void DisableUnusedColliders(int startIndex)
        {
            for (int i = startIndex; i < _colliders.Count; i++)
            {
                _colliders[i].enabled = false;
            }
        }

        /// <summary>
        /// クラスタリングを利用してコライダーの適切な位置を指定
        /// </summary>
        /// <param name="clusteringSample"></param>
        /// <returns></returns>
        private List<Vector3> ClusteringVerts(List<Vector3> clusteringSample)
        {
            int sampleCount = clusteringSample.Count;
            int clusterCount = _colliderNum;

            List<Vector3> centers = new(clusterCount);

            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;

            for (int i = 0; i < sampleCount; i++)
            {
                var s = clusteringSample[i];

                if (s.x > maxX) maxX = s.x;
                if (s.y > maxY) maxY = s.y;
                if (s.z > maxZ) maxZ = s.z;

                if (s.x < minX) minX = s.x;
                if (s.y < minY) minY = s.y;
                if (s.z < minZ) minZ = s.z;
            }

            // ランダムな中心を作成
            for (int i = 0; i < clusterCount - 6; i++)
            {
                centers.Add(new Vector3(
                    Random.Range(minX, maxX),
                    Random.Range(minY, maxY),
                    Random.Range(minZ, maxZ)
                ));
            }

            float midX = (minX + maxX) * 0.5f;
            float midY = (minY + maxY) * 0.5f;
            float midZ = (minZ + maxZ) * 0.5f;

            centers.Add(new Vector3(midX, midY, maxZ));
            centers.Add(new Vector3(midX, midY, minZ));
            centers.Add(new Vector3(midX, maxY, midZ));
            centers.Add(new Vector3(midX, minY, midZ));
            centers.Add(new Vector3(maxX, midY, midZ));
            centers.Add(new Vector3(minX, midY, midZ));

            int[] belongCluster = new int[sampleCount];
            Vector3[] sum = new Vector3[clusterCount];
            int[] count = new int[clusterCount];

            const int maxIteration = 20;
            const float epsilon = 1e-6f;

            for (int iter = 0; iter < maxIteration; iter++)
            {
                // 初期化
                for (int i = 0; i < clusterCount; i++)
                {
                    sum[i] = Vector3.zero;
                    count[i] = 0;
                }

                // 近傍のクラスタを捜索
                for (int i = 0; i < sampleCount; i++)
                {
                    Vector3 point = clusteringSample[i];

                    float minDist = float.MaxValue;
                    int nearest = 0;

                    for (int j = 0; j < clusterCount; j++)
                    {
                        float dist = (centers[j] - point).sqrMagnitude;
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = j;
                        }
                    }

                    belongCluster[i] = nearest;
                    sum[nearest] += point;
                    count[nearest]++;
                }

                // 重心移動
                bool moved = false;

                for (int i = 0; i < clusterCount; i++)
                {
                    if (count[i] == 0) continue;

                    Vector3 newCenter = sum[i] / count[i];

                    if ((newCenter - centers[i]).sqrMagnitude > epsilon)
                    {
                        centers[i] = newCenter;
                        moved = true;
                    }
                }

                if (!moved)
                    break;
            }

            return centers;
        }
    }
}