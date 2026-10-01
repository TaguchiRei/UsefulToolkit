using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

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

        // ── 面分類(ClassifyWholeMeshJob / CutFacePrefixSumJob / WriteWholeTrianglesJob)の結果 ──
        public NativeArray<int> CutFaceCountPerObject;
        public NativeArray<int> CutFaceStartPerObject;

        // 以下の NativeList は、切断三角形の総数が決まる CutFacePrefixSumJob の中でサイズを決める。
        // 後続のJobへは AsDeferredJobArray() で渡し、Job実行時点の長さを読ませること
        // (スケジュール時点では長さ0のため、AsArray() で渡すと空の配列になる)
        public NativeList<int3> CutFaces;
        public NativeList<int> CutStatus;
        public NativeList<int> CutFaceSubmeshId;
        public NativeList<int> CutFaceObjectIndex;

        // ── 断面三角形生成(TriangleCutJob)の結果 ──
        // 切断三角形 i の新規頂点は 2i, 2i+1、新規三角形は 3i..3i+2。断面の辺は常に (2i, 2i+1) になる
        public NativeList<float3> NewVertices;
        public NativeList<float3> NewNormals;
        public NativeList<float2> NewUvs;
        public NativeList<NewTriangle> NewTriangles;

        // ── 断面(キャップ)生成(DistributeAndCapJob)の結果 ──
        public NativeArray<int> CapClosedLoopCount; // per object: 閉じてキャップを生成できたループ数
        public NativeArray<int> CapOpenLoopCount; // per object: 途切れてキャップを生成しなかったループ数

        // ── フラグメント(オブジェクト×表裏)ごとの出力メッシュバッファ ──
        // フラグメントIndex = objIndex * 2 + side (side: 0=front, 1=back)
        // サブメッシュスロット = フラグメントIndex * MaxSubmeshSlots + submesh (submesh: 0..N-1が元サブメッシュ、Nがキャップ)
        //
        // NativeArray<UnsafeList<T>> はunmanaged制約を満たせずコンパイル不可のため、
        // フラグメント・スロットごとに (offset, capacity) で区切ったフラットなリストで表し、実使用数は別配列に書き出す。
        // 容量は ClassifyWholeMeshJob が数えた実数から FragmentLayoutJob が決め、フラットなリストもそこでサイズを決める。
        // そのため後続のJobへは AsDeferredJobArray() で渡すこと(スケジュール時点では長さ0)。

        // ClassifyWholeMeshJob の数え上げ結果
        public NativeArray<int> FragmentWholeVertexCount; // per fragment: 丸ごと入る三角形が使う頂点数(重複除去後)
        public NativeArray<int> FragmentWholeIndexCount; // per slot: 丸ごと入る三角形のインデックス数
        public NativeArray<int> CutFaceCountPerObjectSubmesh; // per (object, submesh) = objIndex * MaxSubmeshSlots + submesh

        public NativeArray<int2> FragmentVertexRange; // per fragment: (offset, capacity) into FragmentVerticesFlat等
        public NativeArray<int> FragmentVertexCount; // per fragment: 実使用頂点数(WriteWholeTrianglesJob→DistributeAndCapJobで引き継ぎ更新)

        public NativeList<float3> FragmentVerticesFlat;
        public NativeList<float3> FragmentNormalsFlat;
        public NativeList<float2> FragmentUvsFlat;

        public NativeArray<int2> FragmentIndexRange; // per slot: (offset, capacity) into FragmentIndicesFlat
        public NativeArray<int> FragmentIndexCount; // per slot: 実使用インデックス数

        public NativeList<int> FragmentIndicesFlat;

        public int MaxSubmeshSlots;

        // ── コライダー用サンプリング点 ──
        // SamplePoints はフラグメントごとに上限ぶん(SampleCapacityPerFragment)を予約した配列で、
        // 実際に使う範囲は SampleRangeJob が SampleRange へ (offset, count) として書き出す
        public NativeArray<float3> SamplePoints;
        public NativeArray<int2> SampleRange;
        public int SampleCapacityPerFragment;

        // ── 最終メッシュ ──
        /// <summary> FinalizeMeshJob が書き込む Mesh.MeshData。適用(ApplyAndDispose)されなかった場合は Dispose で破棄する </summary>
        public Mesh.MeshDataArray WritableMeshData;

        public bool HasWritableMeshData;
        public NativeArray<VertexAttributeDescriptor> VertexLayout;

        public MultiCutContext(int objectCount)
        {
            ObjectCount = objectCount;
        }

        public static int FragmentIndex(int objIndex, int side) => objIndex * 2 + side;

        /// <summary>
        /// フラグメント・スロット単位の表と、空のフラットなリストを確保する。
        /// 容量とリストの長さは、Job内(FragmentLayoutJob)で切断結果の実数から決まる。
        /// </summary>
        public void AllocateFragmentTables(int maxSubmeshSlots)
        {
            MaxSubmeshSlots = maxSubmeshSlots;
            int fragmentCount = ObjectCount * 2;
            int slotCount = fragmentCount * maxSubmeshSlots;

            FragmentWholeVertexCount = new NativeArray<int>(fragmentCount, Allocator.Persistent);
            FragmentWholeIndexCount = new NativeArray<int>(slotCount, Allocator.Persistent);
            CutFaceCountPerObjectSubmesh = new NativeArray<int>(ObjectCount * maxSubmeshSlots, Allocator.Persistent);

            FragmentVertexRange = new NativeArray<int2>(fragmentCount, Allocator.Persistent);
            FragmentVertexCount = new NativeArray<int>(fragmentCount, Allocator.Persistent);
            FragmentIndexRange = new NativeArray<int2>(slotCount, Allocator.Persistent);
            FragmentIndexCount = new NativeArray<int>(slotCount, Allocator.Persistent);

            FragmentVerticesFlat = new NativeList<float3>(Allocator.Persistent);
            FragmentNormalsFlat = new NativeList<float3>(Allocator.Persistent);
            FragmentUvsFlat = new NativeList<float2>(Allocator.Persistent);
            FragmentIndicesFlat = new NativeList<int>(Allocator.Persistent);
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

            if (CapClosedLoopCount.IsCreated) CapClosedLoopCount.Dispose();
            if (CapOpenLoopCount.IsCreated) CapOpenLoopCount.Dispose();

            if (FragmentWholeVertexCount.IsCreated) FragmentWholeVertexCount.Dispose();
            if (FragmentWholeIndexCount.IsCreated) FragmentWholeIndexCount.Dispose();
            if (CutFaceCountPerObjectSubmesh.IsCreated) CutFaceCountPerObjectSubmesh.Dispose();

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

            if (HasWritableMeshData)
            {
                WritableMeshData.Dispose();
                HasWritableMeshData = false;
            }

            if (VertexLayout.IsCreated) VertexLayout.Dispose();
        }
    }
}
