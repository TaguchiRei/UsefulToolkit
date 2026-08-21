using System;
using Unity.Collections;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// MultiMeshCut.Cut 1回の呼び出しで使う全Nativeバッファをまとめたコンテキスト。
    /// 全処理をJob化するため、中間データも全てNative化されている。
    /// </summary>
    public class MultiCutContext : IDisposable
    {
        public readonly int ObjectCount;

        // ── 結合された入力頂点・三角形データ ──
        public NativeArray<float3> BaseVertices;
        public NativeArray<float3> BaseNormals;
        public NativeArray<float2> BaseUvs;
        public NativeArray<int> VertexObjectIndex;
        public NativeArray<int> BaseVertexSide;
        public NativeArray<int2> ObjectVertexRange;

        public NativeArray<int3> AllTriangles;
        public NativeArray<int> AllTriangleSubmesh;
        public NativeArray<int2> ObjectTriangleRange;

        public NativeArray<int> ObjectSubmeshCount;

        /// <summary>
        /// オブジェクトごとの断面(キャップ)を書き込むサブメッシュ番号。
        /// 未切断のメッシュなら新しいスロット(= サブメッシュ数)、既に断面を持つメッシュならそのスロットを再利用する。
        /// フラグメントのサブメッシュ数は常に ObjectCapSlot + 1 になる。
        /// </summary>
        public NativeArray<int> ObjectCapSlot;

        public NativeArray<int> ObjectMeshId;
        public NativeArray<NativeTransform> Transforms;

        /// <summary> オブジェクトごとの切断処理に使う(オブジェクトローカル空間のBlade) </summary>
        public NativeArray<NativePlane> Blades;

        // ── 面分類(Job5a/5b)の結果 ──
        public NativeArray<int> CutFaceCountPerObject;
        public NativeArray<int> CutFaceStartPerObject;
        public int TotalCutFaceCount;

        public NativeArray<int3> CutFaces;
        public NativeArray<int> CutStatus;
        public NativeArray<int> CutFaceSubmeshId;
        public NativeArray<int> CutFaceObjectIndex;

        // ── 断面三角形生成(TriangleCutJob)の結果 ──
        public NativeArray<float3> NewVertices;
        public NativeArray<float3> NewNormals;
        public NativeArray<float2> NewUvs;
        public NativeArray<NewTriangle> NewTriangles;
        public NativeParallelMultiHashMap<int, int2> CutEdges;

        // ── フラグメント(オブジェクト×表裏)ごとの出力メッシュバッファ ──
        // フラグメントIndex = objIndex * 2 + side (side: 0=front, 1=back)
        // NativeArray<UnsafeList<T>> はunmanaged制約を満たせずコンパイル不可のため、
        // 「オブジェクト毎の最悪ケース容量を事前計算 → フラット配列に(offset,capacity)で予約 → 実使用数を別配列に書き出す」
        // 方式で表現する。容量は dedup頂点(≤vertCount) + 断面新規頂点(≤9*triCount) + キャップ頂点(≤6*triCount) の
        // 安全な上限として vertCount + 15*triCount を採用する(triCountは切断三角形数triCountForObjectの安全な上限にもなる)。
        public NativeArray<int2> FragmentVertexRange; // per fragment: (offset, capacity) into FragmentVerticesFlat等
        public NativeArray<int> FragmentVertexCount; // per fragment: 実使用頂点数(ClassifyWholeMeshJob→DistributeAndCapJobで引き継ぎ更新)

        public NativeArray<float3> FragmentVerticesFlat;
        public NativeArray<float3> FragmentNormalsFlat;
        public NativeArray<float2> FragmentUvsFlat;

        // サブメッシュスロット = フラグメントIndex * MaxSubmeshSlots + submesh (submesh: 0..N-1が元サブメッシュ、Nがキャップ)
        public NativeArray<int2> FragmentIndexRange; // per slot: (offset, capacity) into FragmentIndicesFlat
        public NativeArray<int> FragmentIndexCount; // per slot: 実使用インデックス数

        public NativeArray<int> FragmentIndicesFlat;

        public int MaxSubmeshSlots;

        // ── コライダー用サンプリング点 ──
        public NativeArray<float3> SamplePoints;
        public NativeArray<int2> SampleRange;

        public MultiCutContext(int objectCount)
        {
            ObjectCount = objectCount;
        }

        public static int FragmentIndex(int objIndex, int side) => objIndex * 2 + side;

        /// <summary>
        /// ObjectVertexRange/ObjectTriangleRangeが確定した後に呼び出す。
        /// オブジェクト毎の最悪ケース容量からフラグメントバッファのオフセット・容量表を構築する。
        /// </summary>
        public void AllocateFragmentBuffers(int maxSubmeshSlots)
        {
            MaxSubmeshSlots = maxSubmeshSlots;
            int fragmentCount = ObjectCount * 2;
            int slotCount = fragmentCount * maxSubmeshSlots;

            FragmentVertexRange = new NativeArray<int2>(fragmentCount, Allocator.Persistent);
            FragmentVertexCount = new NativeArray<int>(fragmentCount, Allocator.Persistent);
            FragmentIndexRange = new NativeArray<int2>(slotCount, Allocator.Persistent);
            FragmentIndexCount = new NativeArray<int>(slotCount, Allocator.Persistent);

            int vertTotal = 0;
            int idxTotal = 0;

            for (int objIndex = 0; objIndex < ObjectCount; objIndex++)
            {
                int vertCountForObject = ObjectVertexRange[objIndex].y;
                int triCountForObject = ObjectTriangleRange[objIndex].y;

                // 安全な上限: dedup頂点(≤vertCount) + 断面新規頂点(≤9*triCount) + キャップ頂点(≤6*triCount)
                int vertCap = vertCountForObject + 15 * triCountForObject;

                // 安全な上限: 元三角形(≤3*triCount) + 断面新規三角形(≤9*triCount)
                int idxCap = 12 * triCountForObject;

                // 断面スロットは上記に加えてキャップのファン三角形(≤6*triCount)も受け取る。
                // 未切断メッシュでは空きスロットなので実質キャップぶんだけだが、
                // 既に断面を持つメッシュを切り直す場合は同じスロットへ全てが積まれるため、合算した容量が要る。
                int capSlotForObject = ObjectCapSlot[objIndex];
                int capIdxCap = idxCap + 6 * triCountForObject;

                for (int side = 0; side < 2; side++)
                {
                    int fragIdx = objIndex * 2 + side;
                    FragmentVertexRange[fragIdx] = new int2(vertTotal, vertCap);
                    vertTotal += vertCap;

                    for (int s = 0; s < maxSubmeshSlots; s++)
                    {
                        int slot = fragIdx * maxSubmeshSlots + s;
                        int capacity = s == capSlotForObject ? capIdxCap : idxCap;

                        FragmentIndexRange[slot] = new int2(idxTotal, capacity);
                        idxTotal += capacity;
                    }
                }
            }

            FragmentVerticesFlat = new NativeArray<float3>(vertTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            FragmentNormalsFlat = new NativeArray<float3>(vertTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            FragmentUvsFlat = new NativeArray<float2>(vertTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            FragmentIndicesFlat = new NativeArray<int>(idxTotal, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
        }

        public void Dispose()
        {
            if (BaseVertices.IsCreated) BaseVertices.Dispose();
            if (BaseNormals.IsCreated) BaseNormals.Dispose();
            if (BaseUvs.IsCreated) BaseUvs.Dispose();
            if (VertexObjectIndex.IsCreated) VertexObjectIndex.Dispose();
            if (BaseVertexSide.IsCreated) BaseVertexSide.Dispose();
            if (ObjectVertexRange.IsCreated) ObjectVertexRange.Dispose();

            if (AllTriangles.IsCreated) AllTriangles.Dispose();
            if (AllTriangleSubmesh.IsCreated) AllTriangleSubmesh.Dispose();
            if (ObjectTriangleRange.IsCreated) ObjectTriangleRange.Dispose();

            if (ObjectSubmeshCount.IsCreated) ObjectSubmeshCount.Dispose();
            if (ObjectCapSlot.IsCreated) ObjectCapSlot.Dispose();
            if (ObjectMeshId.IsCreated) ObjectMeshId.Dispose();
            if (Transforms.IsCreated) Transforms.Dispose();
            if (Blades.IsCreated) Blades.Dispose();

            if (CutFaceCountPerObject.IsCreated) CutFaceCountPerObject.Dispose();
            if (CutFaceStartPerObject.IsCreated) CutFaceStartPerObject.Dispose();

            if (CutFaces.IsCreated) CutFaces.Dispose();
            if (CutStatus.IsCreated) CutStatus.Dispose();
            if (CutFaceSubmeshId.IsCreated) CutFaceSubmeshId.Dispose();
            if (CutFaceObjectIndex.IsCreated) CutFaceObjectIndex.Dispose();

            if (NewVertices.IsCreated) NewVertices.Dispose();
            if (NewNormals.IsCreated) NewNormals.Dispose();
            if (NewUvs.IsCreated) NewUvs.Dispose();
            if (NewTriangles.IsCreated) NewTriangles.Dispose();
            if (CutEdges.IsCreated) CutEdges.Dispose();

            if (FragmentVertexRange.IsCreated) FragmentVertexRange.Dispose();
            if (FragmentVertexCount.IsCreated) FragmentVertexCount.Dispose();
            if (FragmentVerticesFlat.IsCreated) FragmentVerticesFlat.Dispose();
            if (FragmentNormalsFlat.IsCreated) FragmentNormalsFlat.Dispose();
            if (FragmentUvsFlat.IsCreated) FragmentUvsFlat.Dispose();
            if (FragmentIndexRange.IsCreated) FragmentIndexRange.Dispose();
            if (FragmentIndexCount.IsCreated) FragmentIndexCount.Dispose();
            if (FragmentIndicesFlat.IsCreated) FragmentIndicesFlat.Dispose();

            if (SamplePoints.IsCreated) SamplePoints.Dispose();
            if (SampleRange.IsCreated) SampleRange.Dispose();
        }
    }
}
