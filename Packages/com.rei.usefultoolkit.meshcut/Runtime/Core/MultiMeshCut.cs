using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 複数のCuttableObjectを一枚の刃で一括切断する。
    /// 切断アルゴリズムは全てBurstコンパイル対象のJobチェーンで構成されている。
    /// </summary>
    public class MultiMeshCut
    {
        public bool Complete { private set; get; }
        public Mesh[] CutMesh { private set; get; }
        public List<List<Vector3>> SamplingPoints { private set; get; }

        /// <summary>
        /// フラグメントごとの、再切断用に登録されたメッシュID。添字は CutMesh と同じ。
        /// 切断元が CanMultiCut でない場合、そのフラグメントは登録されないため -1 になる。
        /// </summary>
        public int[] FragmentMeshIds { private set; get; }

        /// <summary> trueにすると各処理段階の所要時間をConsoleへ出力します。 </summary>
        public bool EnableProfileLog;

        private UniTask _cutTask;
        private int _batchCount = 32;
        private int _sampling = 150;

        public UniTask Cut(CuttableObject[] breakables, NativePlane blade)
        {
            Complete = false;
            _cutTask = CutAsync(breakables, blade, _batchCount, _sampling);

            return _cutTask;
        }

        /// <summary>
        /// 頂点単位・三角形単位のJobで使う innerloopBatchCount を登録します。
        /// オブジェクト単位のJobのバッチ数はワーカースレッド数から自動算出されるため、この値の影響を受けません。
        /// </summary>
        /// <param name="batchCount"></param>
        public void SetBatch(int batchCount)
        {
            if (batchCount <= 0)
            {
                Debug.LogWarning("Batch count must be > 0");
                return;
            }

            _batchCount = batchCount;
        }

        /// <summary>
        /// 軽量化メッシュ用サンプリング数を設定します
        /// </summary>
        /// <param name="sampling"></param>
        public void SetSamplingCount(int sampling)
        {
            if (sampling < 10)
            {
                Debug.LogWarning("サンプリング数が少なすぎます");
                return;
            }

            _sampling = sampling;
        }

        private void ProfileLog(string message)
        {
            if (!EnableProfileLog) return;
            Debug.Log($"[UsefulToolkit.MeshCut] {message}");
        }

        /// <summary>
        /// 要素数の少ないIJobParallelFor向けに、全ワーカースレッドへ行き渡るバッチ数を算出します。
        /// </summary>
        private static int CalcBatchCount(int length)
        {
            int workerCount = math.max(1, JobsUtility.JobWorkerCount);
            return math.max(1, length / workerCount);
        }

        private async UniTask CutAsync(CuttableObject[] breakables, NativePlane blade, int batchCount, int sampling)
        {
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            int objectCount = breakables.Length;
            MultiCutContext context = new MultiCutContext(objectCount);

            // オブジェクト単位のJobは1要素あたりの処理が重く、要素数もオブジェクト数しかない。
            // ここにbatchCount(既定32)をそのまま渡すとバッチが1つしか作られずシングルスレッドに退化するため、
            // ワーカースレッド数から分割数を決める。
            int objectBatch = CalcBatchCount(objectCount);

            try
            {
                // 追加登録で膨らんだストアをここで整理する。Jobが走っていないこのタイミングでのみ安全に行える
                MeshDataCache.Instance.RebuildIfNeeded();

                var store = MeshDataCache.Instance.Store;

                Stopwatch stopwatch = Stopwatch.StartNew();

                // [メインスレッド] Unity API(Mesh, Transform)を使う初期化。範囲テーブルとTransformスナップショットのみ。
                context.ObjectMeshId = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.ObjectVertexRange = new NativeArray<int2>(objectCount, Allocator.Persistent);
                context.ObjectTriangleRange = new NativeArray<int2>(objectCount, Allocator.Persistent);
                context.ObjectSubmeshCount = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.ObjectCapSlot = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.Transforms = new NativeArray<NativeTransform>(objectCount, Allocator.Persistent);

                int totalVertexCount = 0;
                int totalTriangleCount = 0;
                int maxSubmeshSlots = 1;

                for (int i = 0; i < objectCount; i++)
                {
                    int meshId = breakables[i].MeshId;
                    context.ObjectMeshId[i] = meshId;

                    int2 vRange = store.MeshVertexRange[meshId];
                    int2 tRange = store.MeshTriangleRange[meshId];
                    int submeshCount = store.MeshSubmeshCount[meshId];

                    context.ObjectVertexRange[i] = new int2(totalVertexCount, vRange.y);
                    context.ObjectTriangleRange[i] = new int2(totalTriangleCount, tRange.y);
                    context.ObjectSubmeshCount[i] = submeshCount;

                    // 既に断面サブメッシュを持つメッシュ(＝一度切られた破片)は、そのスロットへ断面を追記する。
                    // 新しいサブメッシュを足さないので、何度切ってもサブメッシュ数とドローコールが増えない。
                    int capSubmesh = store.MeshCapSubmesh[meshId];
                    int capSlot = capSubmesh >= 0 ? capSubmesh : submeshCount;
                    context.ObjectCapSlot[i] = capSlot;

                    totalVertexCount += vRange.y;
                    totalTriangleCount += tRange.y;
                    maxSubmeshSlots = Mathf.Max(maxSubmeshSlots, capSlot + 1);

                    Transform t = breakables[i].transform;
                    context.Transforms[i] = new NativeTransform(t.position, t.rotation, t.localScale);
                }

                context.BaseVertices = new NativeArray<float3>(totalVertexCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.BaseNormals = new NativeArray<float3>(totalVertexCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.BaseUvs = new NativeArray<float2>(totalVertexCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.VertexObjectIndex = new NativeArray<int>(totalVertexCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.BaseVertexSide = new NativeArray<int>(totalVertexCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                context.AllTriangles = new NativeArray<int3>(totalTriangleCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.AllTriangleSubmesh = new NativeArray<int>(totalTriangleCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                context.Blades = new NativeArray<NativePlane>(objectCount, Allocator.Persistent);

                context.AllocateFragmentBuffers(maxSubmeshSlots);

                ProfileLog($"計測: 初期化処理 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // ── メッシュデータ結合 + Blade変換(並列) ──
                var copyJob = new CopyMeshDataJob
                {
                    SrcVertices = store.Vertices.AsArray(),
                    SrcNormals = store.Normals.AsArray(),
                    SrcUvs = store.Uvs.AsArray(),
                    SrcTriangles = store.Triangles.AsArray(),
                    SrcTriangleSubmesh = store.TriangleSubmesh.AsArray(),
                    SrcMeshVertexRange = store.MeshVertexRange.AsArray(),
                    SrcMeshTriangleRange = store.MeshTriangleRange.AsArray(),
                    ObjectMeshId = context.ObjectMeshId,
                    ObjectVertexRange = context.ObjectVertexRange,
                    ObjectTriangleRange = context.ObjectTriangleRange,
                    DstVertices = context.BaseVertices,
                    DstNormals = context.BaseNormals,
                    DstUvs = context.BaseUvs,
                    VertexObjectIndex = context.VertexObjectIndex,
                    DstTriangles = context.AllTriangles,
                    DstTriangleSubmesh = context.AllTriangleSubmesh
                };
                JobHandle copyHandle = copyJob.Schedule(objectCount, objectBatch);

                var bladeJob = new BladeToLocalJob
                {
                    WorldBlade = blade,
                    Transforms = context.Transforms,
                    Blades = context.Blades
                };
                JobHandle bladeHandle = bladeJob.Schedule(objectCount, objectBatch);

                await JobHandle.CombineDependencies(copyHandle, bladeHandle).ToUniTask(PlayerLoopTiming.Update);

                ProfileLog($"計測: メッシュ結合・Blade変換 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // ── 頂点仕分け ──
                var vertexGetSideJob = new VertexGetSideJob
                {
                    Vertices = context.BaseVertices,
                    BladeIndex = context.VertexObjectIndex,
                    Blades = context.Blades,
                    VertexSides = context.BaseVertexSide
                };

                JobHandle vertexGetSideHandle = vertexGetSideJob.Schedule(totalVertexCount, batchCount);
                await vertexGetSideHandle.ToUniTask(PlayerLoopTiming.Update);

                ProfileLog($"計測: 頂点仕分け処理 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // ── 面分類 + 全表/全裏三角形構築(オブジェクト単位で並列) ──
                context.CutFaceCountPerObject = new NativeArray<int>(objectCount, Allocator.Persistent);

                var classifyJob = new ClassifyWholeMeshJob
                {
                    ObjectVertexRange = context.ObjectVertexRange,
                    ObjectTriangleRange = context.ObjectTriangleRange,
                    AllTriangles = context.AllTriangles,
                    AllTriangleSubmesh = context.AllTriangleSubmesh,
                    BaseVertexSide = context.BaseVertexSide,
                    BaseVertices = context.BaseVertices,
                    BaseNormals = context.BaseNormals,
                    BaseUvs = context.BaseUvs,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentIndexRange = context.FragmentIndexRange,
                    MaxSubmeshSlots = maxSubmeshSlots,
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentNormalsFlat = context.FragmentNormalsFlat,
                    FragmentUvsFlat = context.FragmentUvsFlat,
                    FragmentIndicesFlat = context.FragmentIndicesFlat,
                    FragmentVertexCount = context.FragmentVertexCount,
                    FragmentIndexCount = context.FragmentIndexCount,
                    CutFaceCountPerObject = context.CutFaceCountPerObject
                };

                JobHandle classifyHandle = classifyJob.Schedule(objectCount, objectBatch);
                await classifyHandle.ToUniTask(PlayerLoopTiming.Update);

                ProfileLog($"計測: 面仕分け処理 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // [メインスレッド] オブジェクト毎の切断三角形数からプレフィックス和を計算(軽量な整数演算のみ)
                context.CutFaceStartPerObject = new NativeArray<int>(objectCount, Allocator.Persistent);
                int totalCutFaceCount = 0;

                for (int i = 0; i < objectCount; i++)
                {
                    context.CutFaceStartPerObject[i] = totalCutFaceCount;
                    totalCutFaceCount += context.CutFaceCountPerObject[i];
                }

                context.TotalCutFaceCount = totalCutFaceCount;

                context.CutFaces = new NativeArray<int3>(totalCutFaceCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.CutStatus = new NativeArray<int>(totalCutFaceCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.CutFaceSubmeshId = new NativeArray<int>(totalCutFaceCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.CutFaceObjectIndex = new NativeArray<int>(totalCutFaceCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                var buildCutFaceJob = new BuildCutFaceListJob
                {
                    ObjectTriangleRange = context.ObjectTriangleRange,
                    AllTriangles = context.AllTriangles,
                    AllTriangleSubmesh = context.AllTriangleSubmesh,
                    BaseVertexSide = context.BaseVertexSide,
                    CutFaceStartPerObject = context.CutFaceStartPerObject,
                    CutFaces = context.CutFaces,
                    CutStatus = context.CutStatus,
                    CutFaceSubmeshId = context.CutFaceSubmeshId,
                    CutFaceObjectIndex = context.CutFaceObjectIndex
                };

                JobHandle buildCutFaceHandle = buildCutFaceJob.Schedule(objectCount, objectBatch);
                await buildCutFaceHandle.ToUniTask(PlayerLoopTiming.Update);

                // ── 断面三角形生成 ──
                context.NewVertices = new NativeArray<float3>(totalCutFaceCount * 2, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.NewNormals = new NativeArray<float3>(totalCutFaceCount * 2, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.NewUvs = new NativeArray<float2>(totalCutFaceCount * 2, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.NewTriangles = new NativeArray<NewTriangle>(totalCutFaceCount * 3, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                context.CutEdges =
                    new NativeParallelMultiHashMap<int, int2>(math.max(totalCutFaceCount * 2, 1), Allocator.Persistent);

                var triangleCutJob = new TriangleCutJob
                {
                    CutFaces = context.CutFaces,
                    CutStatus = context.CutStatus,
                    CutFaceSubmeshId = context.CutFaceSubmeshId,
                    Blades = context.Blades,
                    TriangleObjectIndex = context.CutFaceObjectIndex,
                    BaseVertices = context.BaseVertices,
                    BaseNormals = context.BaseNormals,
                    BaseUvs = context.BaseUvs,
                    NewVertices = context.NewVertices,
                    NewNormals = context.NewNormals,
                    NewUvs = context.NewUvs,
                    NewTriangles = context.NewTriangles,
                    CutEdges = context.CutEdges.AsParallelWriter()
                };

                JobHandle triangleCutHandle = triangleCutJob.Schedule(totalCutFaceCount, batchCount);
                await triangleCutHandle.ToUniTask(PlayerLoopTiming.Update);

                ProfileLog($"計測: 面切断処理 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // ── 新規三角形の前後振り分け + 切断面ループ探索 + キャップ生成(オブジェクト単位で並列) ──
                var distributeJob = new DistributeAndCapJob
                {
                    CutFaceStartPerObject = context.CutFaceStartPerObject,
                    CutFaceCountPerObject = context.CutFaceCountPerObject,
                    NewTriangles = context.NewTriangles,
                    NewVertices = context.NewVertices,
                    NewNormals = context.NewNormals,
                    NewUvs = context.NewUvs,
                    BaseVertices = context.BaseVertices,
                    BaseNormals = context.BaseNormals,
                    BaseUvs = context.BaseUvs,
                    Blades = context.Blades,
                    ObjectSubmeshCount = context.ObjectSubmeshCount,
                    ObjectCapSlot = context.ObjectCapSlot,
                    CutEdges = context.CutEdges,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentIndexRange = context.FragmentIndexRange,
                    MaxSubmeshSlots = maxSubmeshSlots,
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentNormalsFlat = context.FragmentNormalsFlat,
                    FragmentUvsFlat = context.FragmentUvsFlat,
                    FragmentIndicesFlat = context.FragmentIndicesFlat,
                    FragmentVertexCount = context.FragmentVertexCount,
                    FragmentIndexCount = context.FragmentIndexCount
                };

                JobHandle distributeHandle = distributeJob.Schedule(objectCount, objectBatch);
                await distributeHandle.ToUniTask(PlayerLoopTiming.Update);

                ProfileLog($"計測: 断面生成 - {stopwatch.ElapsedMilliseconds} ms");
                stopwatch.Restart();

                // [メインスレッド] フラグメント毎の頂点数からサンプリング範囲(コライダー用)を計算
                int fragmentCount = objectCount * 2;
                context.SampleRange = new NativeArray<int2>(fragmentCount, Allocator.Persistent);
                int totalSampleCount = 0;

                for (int i = 0; i < fragmentCount; i++)
                {
                    int vertCount = context.FragmentVertexCount[i];
                    int sampleCount = vertCount <= 200 ? vertCount : sampling;
                    context.SampleRange[i] = new int2(totalSampleCount, sampleCount);
                    totalSampleCount += sampleCount;
                }

                context.SamplePoints = new NativeArray<float3>(totalSampleCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                var sampleJob = new SampleColliderPointsJob
                {
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentVertexCount = context.FragmentVertexCount,
                    SampleRange = context.SampleRange,
                    SamplePoints = context.SamplePoints
                };

                JobHandle sampleHandle = sampleJob.Schedule(fragmentCount, CalcBatchCount(fragmentCount));
                await sampleHandle.ToUniTask(PlayerLoopTiming.Update);

                // [メインスレッド] 公開API(List<List<Vector3>>)の形へ変換
                var samplingPoints = new List<List<Vector3>>(fragmentCount);

                for (int i = 0; i < fragmentCount; i++)
                {
                    int2 range = context.SampleRange[i];
                    var list = new List<Vector3>(range.y);

                    for (int j = 0; j < range.y; j++)
                    {
                        list.Add(context.SamplePoints[range.x + j]);
                    }

                    samplingPoints.Add(list);
                }

                // [メインスレッド] 何回でも切断可能なオブジェクトのフラグメントを、次の切断のためにストアへ登録する。
                // 全てのJobが完了した後に行うこと(ストアのNativeListがリサイズされ、Jobが持つビューが無効になるため)
                int[] fragmentMeshIds = RegisterMultiCutFragments(breakables, context, store, maxSubmeshSlots);

                // FinalizeMeshes は内部でメインスレッドへの切り替えを自前で行う
                CutMesh = await FinalizeMeshes(context, fragmentCount, maxSubmeshSlots);

                FragmentMeshIds = fragmentMeshIds;

                SamplingPoints = samplingPoints;
                Complete = true;

                ProfileLog($"計測: メッシュ生成 - {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                throw new Exception(e.Message);
            }
            finally
            {
                context.Dispose();
                totalStopwatch.Stop();
                ProfileLog($"計測: MultiMeshCut.CutAsync 全体処理時間 - {totalStopwatch.ElapsedMilliseconds} ms");
            }
        }

        /// <summary>
        /// 切断元が CanMultiCut のオブジェクトについて、生成されたフラグメントをストアへ追加登録します。
        /// 登録しなかったフラグメントのIDは -1 になります。
        /// </summary>
        private static int[] RegisterMultiCutFragments(
            CuttableObject[] breakables, MultiCutContext context, NativeMeshDataStore store, int maxSubmeshSlots)
        {
            int objectCount = breakables.Length;
            int[] meshIds = new int[objectCount * 2];

            for (int i = 0; i < meshIds.Length; i++)
            {
                meshIds[i] = -1;
            }

            for (int objIndex = 0; objIndex < objectCount; objIndex++)
            {
                if (!breakables[objIndex].CanMultiCut) continue;

                // 断面スロットは常に最後のサブメッシュ
                int capSlot = context.ObjectCapSlot[objIndex];
                int submeshCount = capSlot + 1;

                for (int side = 0; side < 2; side++)
                {
                    int fragIndex = MultiCutContext.FragmentIndex(objIndex, side);

                    // 刃が実際には切らなかった側は頂点数0になる。中身が無いので登録せず、切断対象からも外す
                    if (context.FragmentVertexCount[fragIndex] == 0) continue;

                    meshIds[fragIndex] = store.AppendFragment(
                        context.FragmentVerticesFlat,
                        context.FragmentNormalsFlat,
                        context.FragmentUvsFlat,
                        context.FragmentVertexRange[fragIndex].x,
                        context.FragmentVertexCount[fragIndex],
                        context.FragmentIndicesFlat,
                        context.FragmentIndexRange,
                        context.FragmentIndexCount,
                        fragIndex * maxSubmeshSlots,
                        submeshCount,
                        capSlot);
                }
            }

            return meshIds;
        }

        private static readonly VertexAttributeDescriptor[] VertexLayout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream: 2)
        };

        private async Awaitable<Mesh[]> FinalizeMeshes(MultiCutContext context, int fragmentCount, int maxSubmeshSlots)
        {
            // AllocateWritableMeshData はメインスレッド必須なので最初に切り替える
            await Awaitable.MainThreadAsync();

            var writableDataArray = Mesh.AllocateWritableMeshData(fragmentCount);

            // 重いメモリコピーをバックグラウンドで実行
            await Awaitable.BackgroundThreadAsync();

            for (int i = 0; i < fragmentCount; i++)
            {
                var data = writableDataArray[i];

                int2 vRange = context.FragmentVertexRange[i];
                int vertexCount = context.FragmentVertexCount[i];

                // Vertex Buffer
                data.SetVertexBufferParams(vertexCount, VertexLayout);

                var vertices = data.GetVertexData<float3>(0);
                var normals = data.GetVertexData<float3>(1);
                var uvs = data.GetVertexData<float2>(2);

                NativeArray<float3>.Copy(context.FragmentVerticesFlat, vRange.x, vertices, 0, vertexCount);
                NativeArray<float3>.Copy(context.FragmentNormalsFlat, vRange.x, normals, 0, vertexCount);
                NativeArray<float2>.Copy(context.FragmentUvsFlat, vRange.x, uvs, 0, vertexCount);

                int objIndex = i / 2;

                // 断面スロットは常に最後のサブメッシュになるので、+1 がサブメッシュ数
                int fragSubmeshCount = context.ObjectCapSlot[objIndex] + 1;

                // Index Buffer
                int totalIndexCount = 0;
                for (int s = 0; s < fragSubmeshCount; s++)
                {
                    totalIndexCount += context.FragmentIndexCount[i * maxSubmeshSlots + s];
                }

                data.SetIndexBufferParams(totalIndexCount, IndexFormat.UInt32);

                var indices = data.GetIndexData<int>();

                // SubMesh
                data.subMeshCount = fragSubmeshCount;

                int indexOffset = 0;

                for (int s = 0; s < fragSubmeshCount; s++)
                {
                    int2 idxRange = context.FragmentIndexRange[i * maxSubmeshSlots + s];
                    int subCount = context.FragmentIndexCount[i * maxSubmeshSlots + s];

                    NativeArray<int>.Copy(context.FragmentIndicesFlat, idxRange.x, indices, indexOffset, subCount);

                    // DontRecalculateBoundsを付けるとBoundsが未計算のままになり、破片がカリングで消える。
                    // ここはバックグラウンドスレッドなので、SetSubMesh側でBoundsを計算させる。
                    data.SetSubMesh(s, new SubMeshDescriptor(indexOffset, subCount));

                    indexOffset += subCount;
                }
            }

            // Mesh生成はメインスレッドで
            await Awaitable.MainThreadAsync();

            Mesh[] resultMeshes = new Mesh[fragmentCount];
            for (int i = 0; i < fragmentCount; i++)
            {
                resultMeshes[i] = new Mesh();
            }

            Mesh.ApplyAndDisposeWritableMeshData(writableDataArray, resultMeshes);

            return resultMeshes;
        }
    }
}
