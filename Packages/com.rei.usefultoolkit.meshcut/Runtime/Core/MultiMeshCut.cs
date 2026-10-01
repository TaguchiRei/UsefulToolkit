using System;
using System.Collections.Generic;
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

        /// <summary> trueにすると各処理段階の所要時間を計測し、切断完了時に表としてConsoleへ出力します。 </summary>
        public bool EnableProfileLog;

        /// <summary> EnableProfileLog が有効な状態で最後に完了した切断の計測結果。未計測なら null。 </summary>
        public MeshCutProfile LastProfile { private set; get; }

        private UniTask _cutTask;
        private int _batchCount = 32;
        private int _sampling = 150;

        public UniTask Cut(CuttableObject[] breakables, NativePlane blade)
        {
            return Cut(breakables, blade, null);
        }

        /// <summary>
        /// 呼び出し側が持つ計測器へ各段階を記録しながら切断します。
        /// profiler を渡した場合、計測結果の組み立て・出力・破棄は呼び出し側が行い、LastProfile は更新しません。
        /// </summary>
        internal UniTask Cut(CuttableObject[] breakables, NativePlane blade, MeshCutProfiler profiler)
        {
            Complete = false;
            _cutTask = CutAsync(breakables, blade, _batchCount, _sampling, profiler);

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

        /// <summary>
        /// 要素数の少ないIJobParallelFor向けに、全ワーカースレッドへ行き渡るバッチ数を算出します。
        /// </summary>
        private static int CalcBatchCount(int length)
        {
            int workerCount = math.max(1, JobsUtility.JobWorkerCount);
            return math.max(1, length / workerCount);
        }

        private async UniTask CutAsync(CuttableObject[] breakables, NativePlane blade, int batchCount, int sampling,
            MeshCutProfiler externalProfiler)
        {
            bool ownsProfiler = externalProfiler == null;
            MeshCutProfiler profiler = externalProfiler ??
                                       (EnableProfileLog ? new MeshCutProfiler() : MeshCutProfiler.Disabled);

            int objectCount = breakables.Length;
            MultiCutContext context = new MultiCutContext(objectCount);

            // オブジェクト単位のJobは1要素あたりの処理が重く、要素数もオブジェクト数しかない。
            // ここにbatchCount(既定32)をそのまま渡すとバッチが1つしか作られずシングルスレッドに退化するため、
            // ワーカースレッド数から分割数を決める。
            int objectBatch = CalcBatchCount(objectCount);

            // スケジュール済みで完了を待っていないJob。finally でバッファを解放する前に必ず完了させる
            JobHandle pendingJobs = default;

            try
            {
                // 追加登録で膨らんだストアをここで整理する。Jobが走っていないこのタイミングでのみ安全に行える
                int rebuildStage = profiler.BeginMain("ストア再構築判定");
                MeshDataCache.Instance.RebuildIfNeeded();
                profiler.EndMain(rebuildStage);

                var store = MeshDataCache.Instance.Store;

                int initStage = profiler.BeginMain("初期化・バッファ確保");

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

                    // 既に断面サブメッシュを持つメッシュ(＝一度切られた破片)は、そのスロットへ断面を追記する(新しいサブメッシュは足さない)。
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

                int fragmentCount = objectCount * 2;

                // 切断三角形数に依存するバッファは、CutFacePrefixSumJob の中でサイズを決める
                context.CutFaceCountPerObject = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.CutFaceStartPerObject = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.CutFaces = new NativeList<int3>(Allocator.Persistent);
                context.CutStatus = new NativeList<int>(Allocator.Persistent);
                context.CutFaceSubmeshId = new NativeList<int>(Allocator.Persistent);
                context.CutFaceObjectIndex = new NativeList<int>(Allocator.Persistent);
                context.NewVertices = new NativeList<float3>(Allocator.Persistent);
                context.NewNormals = new NativeList<float3>(Allocator.Persistent);
                context.NewUvs = new NativeList<float2>(Allocator.Persistent);
                context.NewTriangles = new NativeList<NewTriangle>(Allocator.Persistent);

                context.CapClosedLoopCount = new NativeArray<int>(objectCount, Allocator.Persistent);
                context.CapOpenLoopCount = new NativeArray<int>(objectCount, Allocator.Persistent);

                // サンプリング点は、フラグメント毎に取りうる最大数ぶんを予約しておく
                context.SampleCapacityPerFragment = math.max(SampleRangeJob.FullSampleThreshold, sampling);
                context.SampleRange = new NativeArray<int2>(fragmentCount, Allocator.Persistent);
                context.SamplePoints = new NativeArray<float3>(fragmentCount * context.SampleCapacityPerFragment,
                    Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                // AllocateWritableMeshData はメインスレッド専用のため、Jobをスケジュールする前に確保する
                context.WritableMeshData = Mesh.AllocateWritableMeshData(fragmentCount);
                context.HasWritableMeshData = true;
                context.VertexLayout = new NativeArray<VertexAttributeDescriptor>(VertexLayout, Allocator.Persistent);

                profiler.EndMain(initStage);

                profiler.AddInfo("対象数", objectCount);
                profiler.AddInfo("頂点数", totalVertexCount);
                profiler.AddInfo("三角形数", totalTriangleCount);
                profiler.AddInfo("ワーカースレッド数", JobsUtility.JobWorkerCount);
                profiler.AddInfo("フラグメントバッファ確保量(KB)", CalcFragmentBufferBytes(context) / 1024);

                // ── 全Jobを1本の依存チェーンでスケジュールし、完了はメインスレッドで最後に1回だけ待つ ──

                // メッシュデータ結合 + Blade変換(並列)
                JobHandle copyStart = profiler.BeginJob("メッシュ結合・Blade変換", default, out int copyStage);

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
                JobHandle copyHandle = copyJob.Schedule(objectCount, objectBatch, copyStart);

                var bladeJob = new BladeToLocalJob
                {
                    WorldBlade = blade,
                    Transforms = context.Transforms,
                    Blades = context.Blades
                };
                JobHandle bladeHandle = bladeJob.Schedule(objectCount, objectBatch, copyStart);

                JobHandle handle = profiler.EndJob(copyStage, JobHandle.CombineDependencies(copyHandle, bladeHandle));

                // 頂点仕分け
                var vertexGetSideJob = new VertexGetSideJob
                {
                    Vertices = context.BaseVertices,
                    BladeIndex = context.VertexObjectIndex,
                    Blades = context.Blades,
                    VertexSides = context.BaseVertexSide
                };

                JobHandle vertexGetSideStart =
                    profiler.BeginJob("頂点仕分け", handle, out int vertexGetSideStage, copyStage);
                handle = profiler.EndJob(vertexGetSideStage,
                    vertexGetSideJob.Schedule(totalVertexCount, batchCount, vertexGetSideStart));

                // 面分類 + 全表/全裏三角形構築(オブジェクト単位で並列)
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

                JobHandle classifyStart = profiler.BeginJob("面仕分け", handle, out int classifyStage, vertexGetSideStage);
                handle = profiler.EndJob(classifyStage, classifyJob.Schedule(objectCount, objectBatch, classifyStart));

                // プレフィックス和 + 切断三角形数に応じたリストのサイズ決定
                var prefixSumJob = new CutFacePrefixSumJob
                {
                    CutFaceCountPerObject = context.CutFaceCountPerObject,
                    CutFaceStartPerObject = context.CutFaceStartPerObject,
                    CutFaces = context.CutFaces,
                    CutStatus = context.CutStatus,
                    CutFaceSubmeshId = context.CutFaceSubmeshId,
                    CutFaceObjectIndex = context.CutFaceObjectIndex,
                    NewVertices = context.NewVertices,
                    NewNormals = context.NewNormals,
                    NewUvs = context.NewUvs,
                    NewTriangles = context.NewTriangles
                };

                JobHandle prefixSumStart = profiler.BeginJob("プレフィックス和", handle, out int prefixSumStage, classifyStage);
                handle = profiler.EndJob(prefixSumStage, prefixSumJob.Schedule(prefixSumStart));

                // 切断面リスト構築
                var buildCutFaceJob = new BuildCutFaceListJob
                {
                    ObjectTriangleRange = context.ObjectTriangleRange,
                    AllTriangles = context.AllTriangles,
                    AllTriangleSubmesh = context.AllTriangleSubmesh,
                    BaseVertexSide = context.BaseVertexSide,
                    CutFaceStartPerObject = context.CutFaceStartPerObject,
                    CutFaces = context.CutFaces.AsDeferredJobArray(),
                    CutStatus = context.CutStatus.AsDeferredJobArray(),
                    CutFaceSubmeshId = context.CutFaceSubmeshId.AsDeferredJobArray(),
                    CutFaceObjectIndex = context.CutFaceObjectIndex.AsDeferredJobArray()
                };

                JobHandle buildCutFaceStart =
                    profiler.BeginJob("切断面リスト構築", handle, out int buildCutFaceStage, prefixSumStage);
                handle = profiler.EndJob(buildCutFaceStage,
                    buildCutFaceJob.Schedule(objectCount, objectBatch, buildCutFaceStart));

                // 断面三角形生成(切断三角形数はリストの長さから実行時に決まる)
                var triangleCutJob = new TriangleCutJob
                {
                    CutFaces = context.CutFaces.AsDeferredJobArray(),
                    CutStatus = context.CutStatus.AsDeferredJobArray(),
                    CutFaceSubmeshId = context.CutFaceSubmeshId.AsDeferredJobArray(),
                    Blades = context.Blades,
                    TriangleObjectIndex = context.CutFaceObjectIndex.AsDeferredJobArray(),
                    BaseVertices = context.BaseVertices,
                    BaseNormals = context.BaseNormals,
                    BaseUvs = context.BaseUvs,
                    NewVertices = context.NewVertices.AsDeferredJobArray(),
                    NewNormals = context.NewNormals.AsDeferredJobArray(),
                    NewUvs = context.NewUvs.AsDeferredJobArray(),
                    NewTriangles = context.NewTriangles.AsDeferredJobArray()
                };

                JobHandle triangleCutStart =
                    profiler.BeginJob("面切断", handle, out int triangleCutStage, buildCutFaceStage);
                handle = profiler.EndJob(triangleCutStage,
                    triangleCutJob.Schedule(context.CutFaces, batchCount, triangleCutStart));

                // 新規三角形の前後振り分け + 切断面ループ探索 + キャップ生成(オブジェクト単位で並列)
                var distributeJob = new DistributeAndCapJob
                {
                    CutFaceStartPerObject = context.CutFaceStartPerObject,
                    CutFaceCountPerObject = context.CutFaceCountPerObject,
                    NewTriangles = context.NewTriangles.AsDeferredJobArray(),
                    NewVertices = context.NewVertices.AsDeferredJobArray(),
                    NewNormals = context.NewNormals.AsDeferredJobArray(),
                    NewUvs = context.NewUvs.AsDeferredJobArray(),
                    BaseVertices = context.BaseVertices,
                    BaseNormals = context.BaseNormals,
                    BaseUvs = context.BaseUvs,
                    Blades = context.Blades,
                    ObjectSubmeshCount = context.ObjectSubmeshCount,
                    ObjectCapSlot = context.ObjectCapSlot,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentIndexRange = context.FragmentIndexRange,
                    MaxSubmeshSlots = maxSubmeshSlots,
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentNormalsFlat = context.FragmentNormalsFlat,
                    FragmentUvsFlat = context.FragmentUvsFlat,
                    FragmentIndicesFlat = context.FragmentIndicesFlat,
                    FragmentVertexCount = context.FragmentVertexCount,
                    FragmentIndexCount = context.FragmentIndexCount,
                    CapClosedLoopCount = context.CapClosedLoopCount,
                    CapOpenLoopCount = context.CapOpenLoopCount
                };

                JobHandle distributeStart =
                    profiler.BeginJob("断面生成", handle, out int distributeStage, triangleCutStage);
                JobHandle distributeHandle = profiler.EndJob(distributeStage,
                    distributeJob.Schedule(objectCount, objectBatch, distributeStart));

                // 断面生成の後は、サンプリング(範囲計算→点の抽出)とメッシュ書込が互いに独立なので並行して走らせる
                var sampleRangeJob = new SampleRangeJob
                {
                    FragmentVertexCount = context.FragmentVertexCount,
                    SamplingCount = sampling,
                    CapacityPerFragment = context.SampleCapacityPerFragment,
                    SampleRange = context.SampleRange
                };

                JobHandle sampleRangeStart =
                    profiler.BeginJob("サンプリング範囲計算", distributeHandle, out int sampleRangeStage, distributeStage);
                JobHandle sampleHandle = profiler.EndJob(sampleRangeStage, sampleRangeJob.Schedule(sampleRangeStart));

                var sampleJob = new SampleColliderPointsJob
                {
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentVertexCount = context.FragmentVertexCount,
                    SampleRange = context.SampleRange,
                    SamplePoints = context.SamplePoints
                };

                JobHandle sampleStart =
                    profiler.BeginJob("サンプリング", sampleHandle, out int sampleStage, sampleRangeStage);
                sampleHandle = profiler.EndJob(sampleStage,
                    sampleJob.Schedule(fragmentCount, CalcBatchCount(fragmentCount), sampleStart));

                var finalizeJob = new FinalizeMeshJob
                {
                    MeshData = context.WritableMeshData,
                    VertexLayout = context.VertexLayout,
                    FragmentVertexRange = context.FragmentVertexRange,
                    FragmentVertexCount = context.FragmentVertexCount,
                    FragmentVerticesFlat = context.FragmentVerticesFlat,
                    FragmentNormalsFlat = context.FragmentNormalsFlat,
                    FragmentUvsFlat = context.FragmentUvsFlat,
                    FragmentIndexRange = context.FragmentIndexRange,
                    FragmentIndexCount = context.FragmentIndexCount,
                    FragmentIndicesFlat = context.FragmentIndicesFlat,
                    ObjectCapSlot = context.ObjectCapSlot,
                    MaxSubmeshSlots = maxSubmeshSlots
                };

                JobHandle finalizeStart =
                    profiler.BeginJob("メッシュ書込", distributeHandle, out int finalizeStage, distributeStage);
                JobHandle finalizeHandle = profiler.EndJob(finalizeStage,
                    finalizeJob.Schedule(fragmentCount, CalcBatchCount(fragmentCount), finalizeStart));

                pendingJobs = JobHandle.CombineDependencies(sampleHandle, finalizeHandle);
                JobHandle.ScheduleBatchedJobs();

                await pendingJobs.ToUniTask(PlayerLoopTiming.Update);
                profiler.Observe(sampleStage);
                profiler.Observe(finalizeStage);

                // ── ここからメインスレッド ──
                profiler.AddInfo("切断三角形数", context.CutFaces.Length);

                if (profiler.Enabled)
                {
                    AddCapInfos(context, profiler);
                }

                // 公開API(List<List<Vector3>>)の形へ変換
                int convertStage = profiler.BeginMain("サンプリング点変換");
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

                profiler.EndMain(convertStage);

                // 何回でも切断可能なオブジェクトのフラグメントを、次の切断のためにストアへ登録する。
                // 全てのJobが完了した後に行うこと(ストアのNativeListがリサイズされ、Jobが持つビューが無効になるため)
                int registerStage = profiler.BeginMain("破片のストア登録");
                int[] fragmentMeshIds = RegisterMultiCutFragments(breakables, context, store, maxSubmeshSlots);
                profiler.EndMain(registerStage);

                int fragmentVertexTotal = 0;
                for (int i = 0; i < fragmentCount; i++)
                {
                    fragmentVertexTotal += context.FragmentVertexCount[i];
                }

                profiler.AddInfo("フラグメント頂点数", fragmentVertexTotal);

                // FinalizeMeshJob が書き込んだ MeshData を Mesh へ反映する
                int applyStage = profiler.BeginMain("メッシュ生成・適用");

                Mesh[] resultMeshes = new Mesh[fragmentCount];
                for (int i = 0; i < fragmentCount; i++)
                {
                    resultMeshes[i] = new Mesh();
                }

                Mesh.ApplyAndDisposeWritableMeshData(context.WritableMeshData, resultMeshes);
                context.HasWritableMeshData = false;

                profiler.EndMain(applyStage);

                CutMesh = resultMeshes;
                FragmentMeshIds = fragmentMeshIds;

                SamplingPoints = samplingPoints;
                Complete = true;

                if (ownsProfiler && profiler.Enabled)
                {
                    LastProfile = profiler.Build("MultiMeshCut.Cut");
                    Debug.Log(LastProfile.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                throw new Exception(e.Message);
            }
            finally
            {
                // 例外で抜けた場合もJobが使っているバッファを破棄しないよう、完了を待ってから解放する
                pendingJobs.Complete();
                context.Dispose();

                if (ownsProfiler)
                {
                    profiler.Dispose();
                }
            }
        }

        /// <summary>
        /// 断面ループの集計を計測結果の付帯情報に追加します。
        /// 途切れたループは、辿った向きごとに区間として数えるため、1本の途切れたループが2と数えられることがあります。
        /// </summary>
        private static void AddCapInfos(MultiCutContext context, MeshCutProfiler profiler)
        {
            int closedTotal = 0;
            int openTotal = 0;
            int noCapObjects = 0;

            for (int i = 0; i < context.ObjectCount; i++)
            {
                int closed = context.CapClosedLoopCount[i];

                closedTotal += closed;
                openTotal += context.CapOpenLoopCount[i];

                // 切断面を持つのに断面が1枚も生成されなかった対象
                if (context.CutFaceCountPerObject[i] > 0 && closed == 0)
                {
                    noCapObjects++;
                }
            }

            profiler.AddInfo("閉じた断面ループ数", closedTotal);
            profiler.AddInfo("途切れた断面ループ区間数", openTotal);
            profiler.AddInfo("断面が生成されなかった対象数", noCapObjects);
        }

        /// <summary> フラグメント用フラットバッファ(頂点・法線・UV・インデックス)の確保バイト数 </summary>
        private static long CalcFragmentBufferBytes(MultiCutContext context)
        {
            long vertexBytes = (long)context.FragmentVerticesFlat.Length * (12 + 12 + 8);
            long indexBytes = (long)context.FragmentIndicesFlat.Length * 4;

            return vertexBytes + indexBytes;
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
    }
}
