using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位で、TriangleCutJobが生成した新規三角形をFront/Backフラグメントへ振り分け、
    /// 切断面のループを探索し、ファン三角形で断面(キャップ)を生成する。
    /// フラグメントバッファの頂点/インデックスカーソルはClassifyWholeMeshJobが書き出した値から引き継ぐ。
    /// </summary>
    [BurstCompile]
    public struct DistributeAndCapJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> CutFaceStartPerObject;
        [ReadOnly] public NativeArray<int> CutFaceCountPerObject;

        [ReadOnly] public NativeArray<NewTriangle> NewTriangles;
        [ReadOnly] public NativeArray<float3> NewVertices;
        [ReadOnly] public NativeArray<float3> NewNormals;
        [ReadOnly] public NativeArray<float2> NewUvs;

        [ReadOnly] public NativeArray<float3> BaseVertices;
        [ReadOnly] public NativeArray<float3> BaseNormals;
        [ReadOnly] public NativeArray<float2> BaseUvs;

        [ReadOnly] public NativeArray<NativePlane> Blades;
        [ReadOnly] public NativeArray<int> ObjectSubmeshCount;

        /// <summary> 断面を書き込むサブメッシュ番号。既に断面を持つメッシュではそのスロットを再利用する </summary>
        [ReadOnly] public NativeArray<int> ObjectCapSlot;

        [ReadOnly] public NativeParallelMultiHashMap<int, int2> CutEdges;

        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int2> FragmentIndexRange;
        public int MaxSubmeshSlots;

        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentVerticesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentNormalsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float2> FragmentUvsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndicesFlat;

        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentVertexCount;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndexCount;

        private const float QuantizePrecision = 10000f; // 0.1mm単位で丸める

        public void Execute(int objIndex)
        {
            int start = CutFaceStartPerObject[objIndex];
            int count = CutFaceCountPerObject[objIndex];

            int frontFrag = MultiCutContext.FragmentIndex(objIndex, 0);
            int backFrag = MultiCutContext.FragmentIndex(objIndex, 1);
            int capSubmesh = ObjectCapSlot[objIndex];

            // ClassifyWholeMeshJobが書き出したカーソルを引き継ぐ
            int frontVertCursor = FragmentVertexCount[frontFrag];
            int backVertCursor = FragmentVertexCount[backFrag];

            var frontIdxCursor = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var backIdxCursor = new NativeArray<int>(MaxSubmeshSlots, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            for (int s = 0; s < MaxSubmeshSlots; s++)
            {
                frontIdxCursor[s] = FragmentIndexCount[frontFrag * MaxSubmeshSlots + s];
                backIdxCursor[s] = FragmentIndexCount[backFrag * MaxSubmeshSlots + s];
            }

            // 1) 新規三角形をFront/Backへ振り分け(オブジェクト内は逐次処理なのでアトミック不要)
            for (int i = 0; i < count; i++)
            {
                int cutFaceIdx = start + i;

                for (int k = 0; k < 3; k++)
                {
                    NewTriangle nt = NewTriangles[cutFaceIdx * 3 + k];

                    float3 v1 = GetVertex(nt.Vertex1);
                    float3 v2 = GetVertex(nt.Vertex2);
                    float3 v3 = GetVertex(nt.Vertex3);
                    float3 n1 = GetNormal(nt.Vertex1);
                    float3 n2 = GetNormal(nt.Vertex2);
                    float3 n3 = GetNormal(nt.Vertex3);
                    float2 u1 = GetUv(nt.Vertex1);
                    float2 u2 = GetUv(nt.Vertex2);
                    float2 u3 = GetUv(nt.Vertex3);

                    if (nt.Side == 1)
                    {
                        frontVertCursor = AddFreshTriangle(frontFrag, nt.Submesh, v1, v2, v3, n1, n2, n3, u1, u2, u3,
                            n1, frontVertCursor, frontIdxCursor);
                    }
                    else
                    {
                        backVertCursor = AddFreshTriangle(backFrag, nt.Submesh, v1, v2, v3, n1, n2, n3, u1, u2, u3,
                            n1, backVertCursor, backIdxCursor);
                    }
                }
            }

            // 2) 切断面のループ探索(このオブジェクトのみのスクラッチデータ、Allocator.Temp)
            var posToRep = new NativeParallelHashMap<QuantKey, int>(64, Allocator.Temp);
            var adjacency = new NativeParallelMultiHashMap<int, int>(64, Allocator.Temp);

            if (CutEdges.TryGetFirstValue(objIndex, out int2 edge, out var edgeIt))
            {
                do
                {
                    float3 vA = NewVertices[edge.x];
                    float3 vB = NewVertices[edge.y];

                    QuantKey keyA = Quantize(vA);
                    QuantKey keyB = Quantize(vB);

                    if (!posToRep.TryGetValue(keyA, out int repA))
                    {
                        repA = edge.x;
                        posToRep.Add(keyA, repA);
                    }

                    if (!posToRep.TryGetValue(keyB, out int repB))
                    {
                        repB = edge.y;
                        posToRep.Add(keyB, repB);
                    }

                    if (repA != repB)
                    {
                        adjacency.Add(repA, repB);
                        adjacency.Add(repB, repA);
                    }
                } while (CutEdges.TryGetNextValue(out edge, ref edgeIt));
            }

            // 3) ループを辿りつつファンキャップを生成
            var visited = new NativeParallelHashSet<int2>(64, Allocator.Temp);

            foreach (var kv in adjacency)
            {
                int loopStart = kv.Key;
                int nextStart = kv.Value;

                if (visited.Contains(new int2(loopStart, nextStart))) continue;

                var loop = new NativeList<int>(Allocator.Temp);

                int prev = loopStart;
                int current = nextStart;

                loop.Add(loopStart);
                visited.Add(new int2(loopStart, nextStart));
                visited.Add(new int2(nextStart, loopStart));

                bool closed = false;

                while (true)
                {
                    loop.Add(current);

                    int next = -1;

                    if (adjacency.TryGetFirstValue(current, out int cand, out var candIt))
                    {
                        do
                        {
                            if (cand != prev)
                            {
                                next = cand;
                                break;
                            }
                        } while (adjacency.TryGetNextValue(out cand, ref candIt));
                    }

                    if (next == -1) break;

                    if (next == loopStart)
                    {
                        if (loop.Length >= 3) closed = true;
                        break;
                    }

                    if (visited.Contains(new int2(current, next))) break;

                    visited.Add(new int2(current, next));
                    visited.Add(new int2(next, current));

                    prev = current;
                    current = next;
                }

                if (closed)
                {
                    frontVertCursor = FillCapFan(objIndex, loop, frontFrag, capSubmesh, true, frontVertCursor,
                        frontIdxCursor);
                    backVertCursor = FillCapFan(objIndex, loop, backFrag, capSubmesh, false, backVertCursor,
                        backIdxCursor);
                }

                loop.Dispose();
            }

            visited.Dispose();
            adjacency.Dispose();
            posToRep.Dispose();

            FragmentVertexCount[frontFrag] = frontVertCursor;
            FragmentVertexCount[backFrag] = backVertCursor;

            for (int s = 0; s < MaxSubmeshSlots; s++)
            {
                FragmentIndexCount[frontFrag * MaxSubmeshSlots + s] = frontIdxCursor[s];
                FragmentIndexCount[backFrag * MaxSubmeshSlots + s] = backIdxCursor[s];
            }

            frontIdxCursor.Dispose();
            backIdxCursor.Dispose();
        }

        private static QuantKey Quantize(float3 v)
        {
            return new QuantKey(
                (long)math.round(v.x * QuantizePrecision),
                (long)math.round(v.y * QuantizePrecision),
                (long)math.round(v.z * QuantizePrecision)
            );
        }

        /// <returns>更新後の頂点カーソル</returns>
        private int FillCapFan(
            int objIndex, NativeList<int> loop, int fragIdx, int capSubmesh, bool isFront,
            int vertCursor, NativeArray<int> idxCursor)
        {
            if (loop.Length < 3) return vertCursor;

            NativePlane blade = Blades[objIndex];

            float3 center = float3.zero;
            for (int i = 0; i < loop.Length; i++)
            {
                center += NewVertices[loop[i]];
            }

            center /= loop.Length;

            float3 normal = blade.Normal;

            float3 tangent =
                math.abs(normal.y) > 0.999f
                    ? math.normalize(math.cross(normal, new float3(1, 0, 0)))
                    : math.normalize(math.cross(normal, new float3(0, 1, 0)));

            float3 bitangent = math.normalize(math.cross(normal, tangent));

            float3 faceNormal = isFront ? -blade.Normal : blade.Normal;

            for (int i = 0; i < loop.Length; i++)
            {
                int currentIndex = loop[i];
                int nextIndex = loop[(i + 1) % loop.Length];

                float3 v0 = NewVertices[currentIndex];
                float3 v1 = NewVertices[nextIndex];
                float3 v2 = center;

                float3 d0 = v0 - center;
                float3 d1 = v1 - center;

                float2 uv0 = new float2(0.5f + math.dot(d0, tangent), 0.5f + math.dot(d0, bitangent));
                float2 uv1 = new float2(0.5f + math.dot(d1, tangent), 0.5f + math.dot(d1, bitangent));
                float2 uv2 = new float2(0.5f, 0.5f);

                vertCursor = AddFreshTriangle(
                    fragIdx, capSubmesh,
                    v0, v1, v2,
                    faceNormal, faceNormal, faceNormal,
                    uv0, uv1, uv2,
                    faceNormal, vertCursor, idxCursor);
            }

            return vertCursor;
        }

        /// <summary> dedupなしで3頂点を追加する。更新後の頂点カーソルを返す。 </summary>
        private int AddFreshTriangle(
            int fragIdx, int submesh,
            float3 v1, float3 v2, float3 v3,
            float3 n1, float3 n2, float3 n3,
            float2 u1, float2 u2, float2 u3,
            float3 faceNormal, int vertCursor, NativeArray<int> idxCursor)
        {
            float3 calculatedNormal = math.cross(v2 - v1, v3 - v1);

            int2 vRange = FragmentVertexRange[fragIdx];
            int baseIndex = vertCursor;
            int vBase = vRange.x + baseIndex;

            if (math.dot(calculatedNormal, faceNormal) < 0f)
            {
                FragmentVerticesFlat[vBase + 0] = v3;
                FragmentVerticesFlat[vBase + 1] = v2;
                FragmentVerticesFlat[vBase + 2] = v1;

                FragmentNormalsFlat[vBase + 0] = n3;
                FragmentNormalsFlat[vBase + 1] = n2;
                FragmentNormalsFlat[vBase + 2] = n1;

                FragmentUvsFlat[vBase + 0] = u3;
                FragmentUvsFlat[vBase + 1] = u2;
                FragmentUvsFlat[vBase + 2] = u1;
            }
            else
            {
                FragmentVerticesFlat[vBase + 0] = v1;
                FragmentVerticesFlat[vBase + 1] = v2;
                FragmentVerticesFlat[vBase + 2] = v3;

                FragmentNormalsFlat[vBase + 0] = n1;
                FragmentNormalsFlat[vBase + 1] = n2;
                FragmentNormalsFlat[vBase + 2] = n3;

                FragmentUvsFlat[vBase + 0] = u1;
                FragmentUvsFlat[vBase + 1] = u2;
                FragmentUvsFlat[vBase + 2] = u3;
            }

            vertCursor += 3;

            int2 idxRange = FragmentIndexRange[fragIdx * MaxSubmeshSlots + submesh];
            int cursor = idxCursor[submesh];

            FragmentIndicesFlat[idxRange.x + cursor + 0] = baseIndex + 0;
            FragmentIndicesFlat[idxRange.x + cursor + 1] = baseIndex + 1;
            FragmentIndicesFlat[idxRange.x + cursor + 2] = baseIndex + 2;

            idxCursor[submesh] = cursor + 3;

            return vertCursor;
        }

        private float3 GetVertex(int index) => index < 0 ? BaseVertices[-(index + 1)] : NewVertices[index];
        private float3 GetNormal(int index) => index < 0 ? BaseNormals[-(index + 1)] : NewNormals[index];
        private float2 GetUv(int index) => index < 0 ? BaseUvs[-(index + 1)] : NewUvs[index];
    }
}
