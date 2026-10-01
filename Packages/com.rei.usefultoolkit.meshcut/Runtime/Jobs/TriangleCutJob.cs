using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 刃をまたぐ三角形を切断し、断面側の新規頂点・三角形を生成する。
    /// 切断三角形 i の新規頂点は 2i, 2i+1 で、この2点を結ぶ辺が断面の輪郭の1辺になる。
    /// 切断三角形数は CutFacePrefixSumJob の実行時に決まるため、IJobParallelForDefer として CutFaces のリストで
    /// スケジュールし、配列は AsDeferredJobArray() で受け取る。
    /// </summary>
    [BurstCompile]
    public struct TriangleCutJob : IJobParallelForDefer
    {
        [ReadOnly] public NativeArray<int3> CutFaces;
        [ReadOnly] public NativeArray<int> CutStatus;
        [ReadOnly] public NativeArray<int> CutFaceSubmeshId;
        [ReadOnly] public NativeArray<NativePlane> Blades;
        [ReadOnly] public NativeArray<int> TriangleObjectIndex;

        // CutFaces の頂点は通し番号。ストア上の番号は ObjectStoreVertexOffset[オブジェクト] を足して求める
        [ReadOnly] public NativeArray<int> ObjectStoreVertexOffset;
        [ReadOnly] public NativeArray<float3> StoreVertices;
        [ReadOnly] public NativeArray<float3> StoreNormals;
        [ReadOnly] public NativeArray<float2> StoreUvs;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<float3> NewVertices;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<float3> NewNormals;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<float2> NewUvs;

        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<NewTriangle> NewTriangles;

        /// <summary>
        /// 切断処理を行う
        /// </summary>
        /// <param name="index">三角形番号</param>
        public void Execute(int index)
        {
            int3 face = CutFaces[index]; //処理する三角形を取得
            int status = CutStatus[index]; //どの頂点が孤立しているかの情報を取得する
            int objIndex = TriangleObjectIndex[index];
            NativePlane blade = Blades[objIndex];
            int storeVertexOffset = ObjectStoreVertexOffset[objIndex];
            int submesh = CutFaceSubmeshId[index];

            //計算に適切な順番に頂点をソートするための情報を取得
            int3 order = GetFaceOrder(status);
            bool isFront = GetIsFront(status); //切断面の法線の正面かどうかを取得

            //頂点を以降の処理に適切な順番になるよう取得
            int indexA = face[order.x]; //孤立頂点
            int indexB = face[order.y];
            int indexC = face[order.z];

            //孤立頂点から残り2頂点への辺(A-B, A-C)と刃の交点を新規頂点として求める
            int vertIndexStart = index * 2;
            WriteEdgeIntersection(indexA + storeVertexOffset, indexB + storeVertexOffset, blade, vertIndexStart + 0);
            WriteEdgeIntersection(indexA + storeVertexOffset, indexC + storeVertexOffset, blade, vertIndexStart + 1);

            //後に再構築するために古いインデックスと新しいインデックスを区別する
            //元からあった頂点はインデックスに一律で1を足して-を付ける。
            //再構築する際、元からあった頂点を-のフラグで検知し、正に戻してから1ひくと復元できる
            int oldA = -(indexA + 1);
            int oldB = -(indexB + 1);
            int oldC = -(indexC + 1);
            int newV1 = vertIndexStart;
            int newV2 = vertIndexStart + 1;

            //新規三角形は3つ生成するのでindex*3した位置に各頂点を設定する
            int triIdxStart = index * 3;
            //それぞれ正面かどうかを調べる
            int sideA = isFront ? 1 : 0;
            int sideBC = isFront ? 0 : 1;


            //新規三角形を登録する。登録の際に法線が切断前と同じになるよう反時計回りで登録される。
            NewTriangles[triIdxStart + 0] = new NewTriangle
            {
                Vertex1 = oldA, Vertex2 = newV1, Vertex3 = newV2,
                Submesh = submesh, Side = sideA
            };
            NewTriangles[triIdxStart + 1] = new NewTriangle
            {
                Vertex1 = newV1, Vertex2 = oldB, Vertex3 = newV2,
                Submesh = submesh, Side = sideBC
            };
            NewTriangles[triIdxStart + 2] = new NewTriangle
            {
                Vertex1 = newV2, Vertex2 = oldB, Vertex3 = oldC,
                Submesh = submesh, Side = sideBC
            };
        }

        /// <summary>
        /// 辺(ストア上の頂点番号 index0-index1)と刃の交点の座標・法線・UVを NewVertices 等の outIndex へ書き込む。
        /// 端点は座標の辞書順に並べ替えてから補間する。
        /// 同じ辺を共有する隣の三角形では孤立頂点が反対側の端点になり、補間の向きが逆になる。
        /// 向きが違うと交点が最後の桁でずれ、DistributeAndCapJob の量子化の境目をまたいだときに
        /// 断面ループが途切れてキャップが生成されなくなるため、向きを揃えてビット単位で同じ値にしている。
        /// </summary>
        private void WriteEdgeIntersection(int index0, int index1, NativePlane blade, int outIndex)
        {
            if (IsLexicographicallyLess(StoreVertices[index1], StoreVertices[index0]))
            {
                (index0, index1) = (index1, index0);
            }

            float3 p0 = StoreVertices[index0];
            float3 p1 = StoreVertices[index1];
            float alpha = Intersect(p0, p1, blade);

            NewVertices[outIndex] = math.lerp(p0, p1, alpha);
            NewNormals[outIndex] = math.lerp(StoreNormals[index0], StoreNormals[index1], alpha);
            NewUvs[outIndex] = math.lerp(StoreUvs[index0], StoreUvs[index1], alpha);
        }

        /// <summary> a が b より辞書順(x→y→z)で小さければ true。座標が完全に同じなら false。 </summary>
        private static bool IsLexicographicallyLess(float3 a, float3 b)
        {
            if (a.x != b.x) return a.x < b.x;
            if (a.y != b.y) return a.y < b.y;
            return a.z < b.z;
        }

        /// <summary>
        /// 二つの座標(ベクトル)と面の接点がベクトルの何パーセントの位置にあるかを取得する
        /// -------------------/-----------
        /// </summary>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        /// <param name="plane"></param>
        /// <returns></returns>
        private static float Intersect(float3 p0, float3 p1, NativePlane plane)
        {
            float3 edge = p1 - p0;
            return (-math.dot(plane.Normal, p0) - plane.Distance) / math.dot(plane.Normal, edge);
        }

        /// <summary>
        /// 切断状態(CutStatus)に応じて三角形頂点の順番を決定。Xの値に設定された頂点が孤立頂点で、
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
        private static int3 GetFaceOrder(int status)
        {
            // Determines the isolated vertex (x) and the other two vertices (y, z)
            // p1 = face[0], p2 = face[1], p3 = face[2]
            return status switch
            {
                // p1 (face[0]) is isolated
                4 or 3 => new int3(0, 1, 2), // p1 is isolated. Connect p1-p2 and p1-p3.
                // p2 (face[1]) is isolated
                2 or 5 => new int3(1, 0, 2), // p2 is isolated. Connect p2-p1 and p2-p3.
                // p3 (face[2]) is isolated
                1 or 6 => new int3(2, 0, 1), // p3 is isolated. Connect p3-p1 and p3-p2.
                _ => new int3(0, 0, 0) // Should not happen for cutting cases, represents an unhandled status.
            };
        }

        private static bool GetIsFront(int status)
        {
            // Checks if the isolated vertex is on the 'front' (positive) side of the blade.
            // Based on the 'status' and which vertex is isolated.
            return status switch
            {
                // p1 (face[0]) is isolated
                4 => true, // p1 is 1 (front)
                3 => false, // p1 is 0 (back)
                // p2 (face[1]) is isolated
                2 => true, // p2 is 1 (front)
                5 => false, // p2 is 0 (back)
                // p3 (face[2]) is isolated
                1 => true, // p3 is 1 (front)
                6 => false, // p3 is 0 (back)
                _ => false // Default for unhandled status (e.g. 0 or 7, which aren't cut)
            };
        }
    }
}
