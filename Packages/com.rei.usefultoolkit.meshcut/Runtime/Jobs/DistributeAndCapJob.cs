using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// オブジェクト単位で、TriangleCutJobが生成した新規三角形をFront/Backフラグメントへ振り分け、
    /// 切断面のループを探索し、ファン三角形で断面(キャップ)を生成する。
    /// フラグメントバッファの頂点/インデックスカーソルはWriteWholeTrianglesJobが書き出した値から引き継ぐ。
    /// </summary>
    [BurstCompile]
    public struct DistributeAndCapJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> CutFaceStartPerObject;
        [ReadOnly] public NativeArray<int> CutFaceCountPerObject;

        [ReadOnly] public NativeArray<NewTriangle> NewTriangles;
        [ReadOnly] public NativeArray<float3> NewVertices;
        [ReadOnly] public NativeArray<float3> NewNormals;

        /// <summary> 新規頂点ごとの、切断した元の辺(通し番号の小さい方, 大きい方)。重複除去のキーに使う </summary>
        [ReadOnly] public NativeArray<int2> NewVertexEdge;
        [ReadOnly] public NativeArray<float2> NewUvs;

        // 元からある頂点(NewTriangle の負の番号)は通し番号で、ストア上の番号は ObjectStoreVertexOffset を足して求める
        [ReadOnly] public NativeArray<int> ObjectStoreVertexOffset;
        [ReadOnly] public NativeArray<float3> StoreVertices;
        [ReadOnly] public NativeArray<float3> StoreNormals;
        [ReadOnly] public NativeArray<float2> StoreUvs;

        [ReadOnly] public NativeArray<NativePlane> Blades;
        [ReadOnly] public NativeArray<int> ObjectSubmeshCount;

        /// <summary> 断面を書き込むサブメッシュ番号。既に断面を持つメッシュではそのスロットを再利用する </summary>
        [ReadOnly] public NativeArray<int> ObjectCapSlot;

        [ReadOnly] public NativeArray<int2> FragmentVertexRange;
        [ReadOnly] public NativeArray<int2> FragmentIndexRange;
        public int MaxSubmeshSlots;

        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentVerticesFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float3> FragmentNormalsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<float2> FragmentUvsFlat;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndicesFlat;

        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentVertexCount;
        [NativeDisableParallelForRestriction] public NativeArray<int> FragmentIndexCount;

        /// <summary> オブジェクトごとの、閉じてキャップを生成できた断面ループの数 </summary>
        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CapClosedLoopCount;

        /// <summary> オブジェクトごとの、途中で途切れてキャップを生成しなかった断面ループの数 </summary>
        [NativeDisableParallelForRestriction] [WriteOnly]
        public NativeArray<int> CapOpenLoopCount;

        private const float QuantizePrecision = 10000f; // 0.1mm単位で丸める

        public void Execute(int objIndex)
        {
            int start = CutFaceStartPerObject[objIndex];
            int count = CutFaceCountPerObject[objIndex];

            int frontFrag = MultiCutContext.FragmentIndex(objIndex, 0);
            int backFrag = MultiCutContext.FragmentIndex(objIndex, 1);
            int capSubmesh = ObjectCapSlot[objIndex];
            int storeVertexOffset = ObjectStoreVertexOffset[objIndex];

            // WriteWholeTrianglesJobが書き出したカーソルを引き継ぐ
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

            // 1) 新規三角形をFront/Backへ振り分け(オブジェクト内は逐次処理なのでアトミック不要)。
            // 頂点は表裏それぞれで重複除去する。キーは、元からある頂点なら (通し番号, -1)、
            // 新規頂点なら切断した元の辺 (小さい通し番号, 大きい通し番号)。
            // 座標ではなく元の頂点番号で見るのは、UV の継ぎ目のように位置が同じでも属性の違う頂点をまとめないため
            var frontVertexMap = new NativeHashMap<int2, int>(math.max(count * 4, 16), Allocator.Temp);
            var backVertexMap = new NativeHashMap<int2, int>(math.max(count * 4, 16), Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                int cutFaceIdx = start + i;

                for (int k = 0; k < 3; k++)
                {
                    NewTriangle nt = NewTriangles[cutFaceIdx * 3 + k];

                    if (nt.Side == 1)
                    {
                        frontVertCursor = AddSideTriangle(frontFrag, nt, storeVertexOffset, frontVertexMap,
                            frontVertCursor, frontIdxCursor);
                    }
                    else
                    {
                        backVertCursor = AddSideTriangle(backFrag, nt, storeVertexOffset, backVertexMap,
                            backVertCursor, backIdxCursor);
                    }
                }
            }

            frontVertexMap.Dispose();
            backVertexMap.Dispose();

            // 2) 切断面のループ探索(このオブジェクトのみのスクラッチデータ、Allocator.Temp)
            var posToRep = new NativeParallelHashMap<QuantKey, int>(64, Allocator.Temp);
            var adjacency = new NativeParallelMultiHashMap<int, int>(64, Allocator.Temp);

            // 切断三角形 i の断面の辺は新規頂点 (2i, 2i+1) を結ぶ辺(TriangleCutJob 参照)。
            // 隣り合う切断三角形の同じ位置の頂点は量子化した座標で同一視し、代表頂点どうしの隣接として登録する
            for (int i = 0; i < count; i++)
            {
                int cutFaceIdx = start + i;
                int vertA = cutFaceIdx * 2;
                int vertB = cutFaceIdx * 2 + 1;

                QuantKey keyA = Quantize(NewVertices[vertA]);
                QuantKey keyB = Quantize(NewVertices[vertB]);

                if (!posToRep.TryGetValue(keyA, out int repA))
                {
                    repA = vertA;
                    posToRep.Add(keyA, repA);
                }

                if (!posToRep.TryGetValue(keyB, out int repB))
                {
                    repB = vertB;
                    posToRep.Add(keyB, repB);
                }

                if (repA != repB)
                {
                    adjacency.Add(repA, repB);
                    adjacency.Add(repB, repA);
                }
            }

            // 3) ループを辿りつつファンキャップを生成
            var visited = new NativeParallelHashSet<int2>(64, Allocator.Temp);

            int closedLoopCount = 0;
            int openLoopCount = 0;

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
                        // 始点へ戻る辺も探索済みにする。記録しないと、この辺を起点に同じループを逆向きに辿り直し、
                        // 途中の探索済みの辺で止まって「途切れたループ」として数えてしまう
                        visited.Add(new int2(current, loopStart));
                        visited.Add(new int2(loopStart, current));

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
                    closedLoopCount++;
                    frontVertCursor = FillCapFan(objIndex, loop, frontFrag, capSubmesh, true, frontVertCursor,
                        frontIdxCursor);
                    backVertCursor = FillCapFan(objIndex, loop, backFrag, capSubmesh, false, backVertCursor,
                        backIdxCursor);
                }
                else
                {
                    openLoopCount++;
                }

                loop.Dispose();
            }

            CapClosedLoopCount[objIndex] = closedLoopCount;
            CapOpenLoopCount[objIndex] = openLoopCount;

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

        /// <summary>
        /// 閉じたループをファン三角形で塞ぐ。頂点はループの各頂点に1つずつと中心に1つを追加し、三角形間で共有する。
        /// 断面の頂点は法線が側面と異なるため、側面の頂点とは共有しない。
        /// </summary>
        /// <returns>更新後の頂点カーソル</returns>
        private int FillCapFan(
            int objIndex, NativeList<int> loop, int fragIdx, int capSubmesh, bool isFront,
            int vertCursor, NativeArray<int> idxCursor)
        {
            int loopLength = loop.Length;
            if (loopLength < 3) return vertCursor;

            NativePlane blade = Blades[objIndex];

            float3 center = float3.zero;
            for (int i = 0; i < loopLength; i++)
            {
                center += NewVertices[loop[i]];
            }

            center /= loopLength;

            float3 normal = blade.Normal;

            float3 tangent =
                math.abs(normal.y) > 0.999f
                    ? math.normalize(math.cross(normal, new float3(1, 0, 0)))
                    : math.normalize(math.cross(normal, new float3(0, 1, 0)));

            float3 bitangent = math.normalize(math.cross(normal, tangent));

            float3 faceNormal = isFront ? -blade.Normal : blade.Normal;

            // ループの i 番目の頂点を baseIndex + i、中心を baseIndex + loopLength に置く
            int2 vRange = FragmentVertexRange[fragIdx];
            int baseIndex = vertCursor;

            for (int i = 0; i < loopLength; i++)
            {
                float3 v = NewVertices[loop[i]];
                float3 d = v - center;

                WriteVertex(vRange.x + baseIndex + i, v, faceNormal,
                    new float2(0.5f + math.dot(d, tangent), 0.5f + math.dot(d, bitangent)));
            }

            int centerIndex = baseIndex + loopLength;
            WriteVertex(vRange.x + centerIndex, center, faceNormal, new float2(0.5f, 0.5f));

            for (int i = 0; i < loopLength; i++)
            {
                int next = (i + 1) % loopLength;

                AddTriangleIndices(fragIdx, capSubmesh,
                    NewVertices[loop[i]], NewVertices[loop[next]], center,
                    baseIndex + i, baseIndex + next, centerIndex,
                    faceNormal, idxCursor);
            }

            return centerIndex + 1;
        }

        /// <summary>
        /// 側面の新規三角形1つを、頂点を重複除去しながら追加する。更新後の頂点カーソルを返す。
        /// 三角形の向きは、1つ目の頂点の法線と面の向きが逆なら裏返す。
        /// </summary>
        private int AddSideTriangle(
            int fragIdx, NewTriangle nt, int storeVertexOffset, NativeHashMap<int2, int> vertexMap,
            int vertCursor, NativeArray<int> idxCursor)
        {
            int i1 = GetOrAddSideVertex(fragIdx, nt.Vertex1, storeVertexOffset, vertexMap, ref vertCursor);
            int i2 = GetOrAddSideVertex(fragIdx, nt.Vertex2, storeVertexOffset, vertexMap, ref vertCursor);
            int i3 = GetOrAddSideVertex(fragIdx, nt.Vertex3, storeVertexOffset, vertexMap, ref vertCursor);

            AddTriangleIndices(fragIdx, nt.Submesh,
                GetVertex(nt.Vertex1, storeVertexOffset),
                GetVertex(nt.Vertex2, storeVertexOffset),
                GetVertex(nt.Vertex3, storeVertexOffset),
                i1, i2, i3,
                GetNormal(nt.Vertex1, storeVertexOffset), idxCursor);

            return vertCursor;
        }

        /// <summary>
        /// 側面の頂点を表す番号(負なら元からある頂点、0以上なら新規頂点)を、フラグメント内の頂点番号へ変換する。
        /// まだ追加していなければ追加する。
        /// </summary>
        private int GetOrAddSideVertex(
            int fragIdx, int vertexRef, int storeVertexOffset, NativeHashMap<int2, int> vertexMap, ref int vertCursor)
        {
            int2 key = vertexRef < 0 ? new int2(-(vertexRef + 1), -1) : NewVertexEdge[vertexRef];

            if (vertexMap.TryGetValue(key, out int existing)) return existing;

            int newIndex = vertCursor;
            WriteVertex(FragmentVertexRange[fragIdx].x + newIndex,
                GetVertex(vertexRef, storeVertexOffset),
                GetNormal(vertexRef, storeVertexOffset),
                GetUv(vertexRef, storeVertexOffset));

            vertexMap.Add(key, newIndex);
            vertCursor++;

            return newIndex;
        }

        private void WriteVertex(int flatIndex, float3 position, float3 normal, float2 uv)
        {
            FragmentVerticesFlat[flatIndex] = position;
            FragmentNormalsFlat[flatIndex] = normal;
            FragmentUvsFlat[flatIndex] = uv;
        }

        /// <summary>
        /// 三角形のインデックスを submesh のスロットへ追加する。
        /// 頂点座標から求めた面の向きが faceNormal と逆なら、頂点の順番を逆にして裏返す。
        /// </summary>
        private void AddTriangleIndices(
            int fragIdx, int submesh,
            float3 v1, float3 v2, float3 v3,
            int i1, int i2, int i3,
            float3 faceNormal, NativeArray<int> idxCursor)
        {
            float3 calculatedNormal = math.cross(v2 - v1, v3 - v1);

            if (math.dot(calculatedNormal, faceNormal) < 0f)
            {
                (i1, i3) = (i3, i1);
            }

            int2 idxRange = FragmentIndexRange[fragIdx * MaxSubmeshSlots + submesh];
            int cursor = idxCursor[submesh];

            FragmentIndicesFlat[idxRange.x + cursor + 0] = i1;
            FragmentIndicesFlat[idxRange.x + cursor + 1] = i2;
            FragmentIndicesFlat[idxRange.x + cursor + 2] = i3;

            idxCursor[submesh] = cursor + 3;
        }

        // index が負なら元からある頂点(通し番号 -(index + 1))、0以上なら TriangleCutJob が生成した新規頂点
        private float3 GetVertex(int index, int storeVertexOffset) =>
            index < 0 ? StoreVertices[-(index + 1) + storeVertexOffset] : NewVertices[index];

        private float3 GetNormal(int index, int storeVertexOffset) =>
            index < 0 ? StoreNormals[-(index + 1) + storeVertexOffset] : NewNormals[index];

        private float2 GetUv(int index, int storeVertexOffset) =>
            index < 0 ? StoreUvs[-(index + 1) + storeVertexOffset] : NewUvs[index];
    }
}
