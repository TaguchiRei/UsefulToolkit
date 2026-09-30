using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace UsefulToolkit.MeshCut
{
    /// <summary>
    /// 切断処理の各段階の時刻を記録し、MeshCutProfile を組み立てる。
    /// 時刻は全て Stopwatch.GetTimestamp() で取るため、メインスレッド・ワーカー・バックグラウンドの値を直接比較できる。
    /// Disabled のときは全メソッドが何もしない(Jobの前後に計測用Jobも挟まない)。
    /// </summary>
    internal sealed class MeshCutProfiler : IDisposable
    {
        /// <summary> 何も記録しないインスタンス。状態を持たないので共有してよい。 </summary>
        public static readonly MeshCutProfiler Disabled = new(false);

        private const int MaxJobStamps = 64;

        private readonly bool _enabled;
        private readonly long _originTimestamp;
        private readonly int _originFrame;

        private readonly List<Entry> _entries = new();
        private readonly List<MeshCutProfileInfo> _infos = new();

        /// <summary> Jobの開始・終了時刻の書き込み先。1段階につき2要素(開始, 終了)を使う </summary>
        private NativeArray<long> _jobStamps;

        private int _jobStampCursor;

        public bool Enabled => _enabled;

        public MeshCutProfiler() : this(true)
        {
        }

        private MeshCutProfiler(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) return;

            _originTimestamp = Stopwatch.GetTimestamp();
            _originFrame = Time.frameCount;
        }

        private sealed class Entry
        {
            public string Name;
            public MeshCutStageKind Kind;
            public bool IsBreakdown;

            public long Request;
            public long Start;
            public long End;
            public long Observed;
            public int Frame = -1;

            /// <summary> Jobの場合、_jobStamps 上の開始時刻の位置(終了時刻はその次)。Job以外は -1 </summary>
            public int JobStampIndex = -1;

            /// <summary> 実行時間を別途集計した段階(複数フレームにまたがる反映処理など)の値。未使用は -1 </summary>
            public long ExecuteTicksOverride = -1;
        }

        public void AddInfo(string label, long value)
        {
            if (!_enabled) return;

            _infos.Add(new MeshCutProfileInfo(label, value));
        }

        // ── メインスレッド / バックグラウンド ──

        /// <summary> メインスレッドで今から同期的に実行する段階を開始します。EndMain で閉じてください。 </summary>
        public int BeginMain(string name)
        {
            if (!_enabled) return -1;

            long now = Stopwatch.GetTimestamp();
            return AddEntry(new Entry { Name = name, Kind = MeshCutStageKind.Main, Request = now, Start = now });
        }

        /// <summary> メインスレッドの段階を終了します。メインスレッドで実行するので、終了と検知は同時とみなします。 </summary>
        public void EndMain(int id)
        {
            if (!_enabled || id < 0) return;

            Entry entry = _entries[id];
            entry.End = Stopwatch.GetTimestamp();
            entry.Observed = entry.End;
            entry.Frame = Time.frameCount - _originFrame;
        }

        /// <summary>
        /// スレッド切り替えや待機など、実行開始までに待ちが入る段階を要求します。
        /// 実行が始まったら MarkStart、終わったら MarkEnd、メインスレッドで完了を検知したら Observe を呼んでください。
        /// </summary>
        public int Request(string name, MeshCutStageKind kind)
        {
            if (!_enabled) return -1;

            return AddEntry(new Entry { Name = name, Kind = kind, Request = Stopwatch.GetTimestamp() });
        }

        /// <summary> 任意のスレッドから呼べます。 </summary>
        public void MarkStart(int id)
        {
            if (!_enabled || id < 0) return;

            _entries[id].Start = Stopwatch.GetTimestamp();
        }

        /// <summary> 任意のスレッドから呼べます。 </summary>
        public void MarkEnd(int id)
        {
            if (!_enabled || id < 0) return;

            _entries[id].End = Stopwatch.GetTimestamp();
        }

        /// <summary> メインスレッドから呼んでください(フレーム数を記録するため)。 </summary>
        public void Observe(int id)
        {
            if (!_enabled || id < 0) return;

            Entry entry = _entries[id];
            entry.Observed = Stopwatch.GetTimestamp();
            entry.Frame = Time.frameCount - _originFrame;
        }

        /// <summary>
        /// 複数フレームに分けて実行した処理を1段階として記録します。
        /// 実行時間は executeTicks、待ちは区間の長さから実行時間を引いた値になります。メインスレッドから呼んでください。
        /// </summary>
        public void AddAccumulated(string name, long startTimestamp, long endTimestamp, long executeTicks,
            bool isBreakdown = false)
        {
            if (!_enabled) return;

            AddEntry(new Entry
            {
                Name = name,
                Kind = MeshCutStageKind.Main,
                IsBreakdown = isBreakdown,
                Request = startTimestamp,
                Start = startTimestamp,
                End = endTimestamp,
                Observed = endTimestamp,
                Frame = Time.frameCount - _originFrame,
                ExecuteTicksOverride = executeTicks
            });
        }

        // ── Job ──

        /// <summary>
        /// Jobの段階を開始します。返したハンドルを、この段階でスケジュールする全Jobの依存に渡してください。
        /// 有効時は dependsOn の後に開始時刻を記録するJobを挟み、無効時は dependsOn をそのまま返します。
        /// </summary>
        public JobHandle BeginJob(string name, JobHandle dependsOn, out int id)
        {
            id = -1;
            if (!_enabled) return dependsOn;

            if (!_jobStamps.IsCreated)
            {
                _jobStamps = new NativeArray<long>(MaxJobStamps, Allocator.Persistent);
            }

            var entry = new Entry { Name = name, Kind = MeshCutStageKind.Job, Request = Stopwatch.GetTimestamp() };
            id = AddEntry(entry);

            if (_jobStampCursor + 2 > MaxJobStamps)
            {
                UnityEngine.Debug.LogWarning($"[UsefulToolkit.MeshCut] 計測できるJobの段階数を超えたため、{name} の実行時間は記録しません。");
                return dependsOn;
            }

            entry.JobStampIndex = _jobStampCursor;
            _jobStampCursor += 2;

            return new TimestampJob { Stamps = _jobStamps, Index = entry.JobStampIndex }.Schedule(dependsOn);
        }

        /// <summary>
        /// Jobの段階を終了します。この段階のJobのハンドルを渡し、戻り値のハンドルの完了を待ってください。
        /// 有効時は終了時刻を記録するJobを後ろに挟みます。
        /// </summary>
        public JobHandle EndJob(int id, JobHandle stageHandle)
        {
            if (!_enabled || id < 0) return stageHandle;

            Entry entry = _entries[id];
            if (entry.JobStampIndex < 0) return stageHandle;

            return new TimestampJob { Stamps = _jobStamps, Index = entry.JobStampIndex + 1 }.Schedule(stageHandle);
        }

        // ── 結果 ──

        /// <summary>
        /// 記録した内容から計測結果を作ります。メインスレッドから、全てのJobの完了後に呼んでください。
        /// 無効時は null を返します。
        /// </summary>
        public MeshCutProfile Build(string title)
        {
            if (!_enabled) return null;

            var stages = new List<MeshCutStageRecord>(_entries.Count);
            long lastTimestamp = _originTimestamp;

            foreach (Entry entry in _entries)
            {
                long start = entry.Start;
                long end = entry.End;

                if (entry.JobStampIndex >= 0)
                {
                    start = _jobStamps[entry.JobStampIndex];
                    end = _jobStamps[entry.JobStampIndex + 1];
                }

                long wait;
                long execute;

                if (entry.IsBreakdown)
                {
                    // 内訳は親の段階と同じ区間を共有するため、待ちは持たない(実行時間だけが意味を持つ)
                    execute = entry.ExecuteTicksOverride >= 0 ? entry.ExecuteTicksOverride : end - start;
                    wait = 0;
                }
                else if (entry.ExecuteTicksOverride >= 0)
                {
                    execute = entry.ExecuteTicksOverride;
                    wait = Math.Max(0L, end - start - execute);
                }
                else
                {
                    execute = end - start;
                    wait = start - entry.Request;
                }

                long observed = entry.Observed != 0 ? entry.Observed : end;
                lastTimestamp = Math.Max(lastTimestamp, observed);

                stages.Add(new MeshCutStageRecord(
                    entry.Name, entry.Kind, entry.IsBreakdown,
                    ToMs(wait), ToMs(execute), ToMs(observed - end), entry.Frame));
            }

            return new MeshCutProfile(
                title,
                _infos.ToArray(),
                stages,
                ToMs(lastTimestamp - _originTimestamp),
                Time.frameCount - _originFrame);
        }

        public void Dispose()
        {
            if (_jobStamps.IsCreated) _jobStamps.Dispose();
        }

        private int AddEntry(Entry entry)
        {
            _entries.Add(entry);
            return _entries.Count - 1;
        }

        private static double ToMs(long ticks)
        {
            return ticks * 1000d / Stopwatch.Frequency;
        }
    }
}
