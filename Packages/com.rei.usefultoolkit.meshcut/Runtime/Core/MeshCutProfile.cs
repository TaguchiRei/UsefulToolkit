using System;
using System.Collections.Generic;
using System.Text;

namespace UsefulToolkit.MeshCut
{
    /// <summary> 計測した段階がどこで実行されたか。 </summary>
    public enum MeshCutStageKind
    {
        /// <summary> メインスレッド </summary>
        Main,

        /// <summary> Job(ワーカースレッド) </summary>
        Job,

        /// <summary> Awaitable.BackgroundThreadAsync 上のバックグラウンドスレッド </summary>
        Background
    }

    /// <summary> 計測した1段階ぶんの結果。時間の単位は全てミリ秒。 </summary>
    public readonly struct MeshCutStageRecord
    {
        public readonly string Name;
        public readonly MeshCutStageKind Kind;

        /// <summary> 直前の段階の内訳であれば true。合計値には含めない。 </summary>
        public readonly bool IsBreakdown;

        /// <summary> 実行を要求(Jobのスケジュール・スレッド切り替え・待機開始)してから、実際に実行が始まるまでの時間 </summary>
        public readonly double WaitMs;

        /// <summary> 実行していた時間 </summary>
        public readonly double ExecuteMs;

        /// <summary> 実行が終わってから、メインスレッドがその完了を検知するまでの時間 </summary>
        public readonly double DetectDelayMs;

        /// <summary> 計測開始から数えた、この段階を検知(または終了)したフレーム数 </summary>
        public readonly int Frame;

        public MeshCutStageRecord(
            string name, MeshCutStageKind kind, bool isBreakdown,
            double waitMs, double executeMs, double detectDelayMs, int frame)
        {
            Name = name;
            Kind = kind;
            IsBreakdown = isBreakdown;
            WaitMs = waitMs;
            ExecuteMs = executeMs;
            DetectDelayMs = detectDelayMs;
            Frame = frame;
        }
    }

    /// <summary> 計測結果に添える数値情報(対象数・頂点数など)。 </summary>
    public readonly struct MeshCutProfileInfo
    {
        public readonly string Label;
        public readonly long Value;

        public MeshCutProfileInfo(string label, long value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// 切断1回ぶんの処理時間の計測結果。
    /// メインスレッドの処理・Job・バックグラウンド処理の全段階を、要求→開始→終了→検知の時刻で表す。
    /// ToString() で Console 向けの表を返す。
    /// </summary>
    public sealed class MeshCutProfile
    {
        public string Title { get; }
        public IReadOnlyList<MeshCutProfileInfo> Infos { get; }
        public IReadOnlyList<MeshCutStageRecord> Stages { get; }

        /// <summary> 計測開始から最後の段階を検知するまでの経過時間 </summary>
        public double ElapsedMs { get; }

        /// <summary> 計測開始から終了までに経過したフレーム数 </summary>
        public int ElapsedFrames { get; }

        /// <summary> メインスレッドで実行していた時間の合計(内訳行を除く) </summary>
        public double MainExecuteMs { get; }

        /// <summary> Job・バックグラウンドスレッドで実行していた時間の合計 </summary>
        public double WorkerExecuteMs { get; }

        /// <summary> 経過時間のうち、どの段階も実行していなかった時間 </summary>
        public double IdleMs { get; }

        public MeshCutProfile(
            string title,
            IReadOnlyList<MeshCutProfileInfo> infos,
            IReadOnlyList<MeshCutStageRecord> stages,
            double elapsedMs,
            int elapsedFrames)
            : this(title, infos, stages, elapsedMs, elapsedFrames,
                SumExecute(stages, MeshCutStageKind.Main),
                SumExecute(stages, MeshCutStageKind.Job) + SumExecute(stages, MeshCutStageKind.Background))
        {
        }

        private MeshCutProfile(
            string title,
            IReadOnlyList<MeshCutProfileInfo> infos,
            IReadOnlyList<MeshCutStageRecord> stages,
            double elapsedMs,
            int elapsedFrames,
            double mainExecuteMs,
            double workerExecuteMs)
        {
            Title = title;
            Infos = infos;
            Stages = stages;
            ElapsedMs = elapsedMs;
            ElapsedFrames = elapsedFrames;
            MainExecuteMs = mainExecuteMs;
            WorkerExecuteMs = workerExecuteMs;
            IdleMs = Math.Max(0d, elapsedMs - mainExecuteMs - workerExecuteMs);
        }

        private static double SumExecute(IReadOnlyList<MeshCutStageRecord> stages, MeshCutStageKind kind)
        {
            double sum = 0d;

            foreach (MeshCutStageRecord stage in stages)
            {
                if (stage.IsBreakdown || stage.Kind != kind) continue;
                sum += stage.ExecuteMs;
            }

            return sum;
        }

        /// <summary>
        /// 同じ条件で複数回計測した結果から、各値の中央値をとった結果を作ります。
        /// 段階は先頭の結果の並びを基準にし、同じ位置に同じ名前の段階を持つ結果だけを集計します。
        /// </summary>
        public static MeshCutProfile Median(IReadOnlyList<MeshCutProfile> profiles, string title)
        {
            if (profiles == null || profiles.Count == 0)
            {
                throw new ArgumentException("計測結果が1件もありません。", nameof(profiles));
            }

            MeshCutProfile first = profiles[0];

            var infos = new List<MeshCutProfileInfo>(first.Infos.Count);

            for (int i = 0; i < first.Infos.Count; i++)
            {
                MeshCutProfileInfo info = first.Infos[i];
                var values = new List<double>(profiles.Count);

                foreach (MeshCutProfile profile in profiles)
                {
                    if (i < profile.Infos.Count && profile.Infos[i].Label == info.Label)
                    {
                        values.Add(profile.Infos[i].Value);
                    }
                }

                infos.Add(new MeshCutProfileInfo(info.Label, (long)Math.Round(MedianOf(values))));
            }

            var stages = new List<MeshCutStageRecord>(first.Stages.Count);

            for (int i = 0; i < first.Stages.Count; i++)
            {
                MeshCutStageRecord stage = first.Stages[i];

                var waits = new List<double>(profiles.Count);
                var executes = new List<double>(profiles.Count);
                var detects = new List<double>(profiles.Count);
                var frames = new List<double>(profiles.Count);

                foreach (MeshCutProfile profile in profiles)
                {
                    if (i >= profile.Stages.Count || profile.Stages[i].Name != stage.Name) continue;

                    MeshCutStageRecord s = profile.Stages[i];
                    waits.Add(s.WaitMs);
                    executes.Add(s.ExecuteMs);
                    detects.Add(s.DetectDelayMs);
                    frames.Add(s.Frame);
                }

                stages.Add(new MeshCutStageRecord(
                    stage.Name, stage.Kind, stage.IsBreakdown,
                    MedianOf(waits), MedianOf(executes), MedianOf(detects), (int)Math.Round(MedianOf(frames))));
            }

            var elapsed = new List<double>(profiles.Count);
            var elapsedFrames = new List<double>(profiles.Count);
            var mains = new List<double>(profiles.Count);
            var workers = new List<double>(profiles.Count);

            foreach (MeshCutProfile profile in profiles)
            {
                elapsed.Add(profile.ElapsedMs);
                elapsedFrames.Add(profile.ElapsedFrames);
                mains.Add(profile.MainExecuteMs);
                workers.Add(profile.WorkerExecuteMs);
            }

            // 合計値は段階ごとの中央値を足し直すのではなく、各回の合計値の中央値をとる
            return new MeshCutProfile(
                title, infos, stages,
                MedianOf(elapsed), (int)Math.Round(MedianOf(elapsedFrames)),
                MedianOf(mains), MedianOf(workers));
        }

        private static double MedianOf(List<double> values)
        {
            if (values.Count == 0) return 0d;

            values.Sort();
            int mid = values.Count / 2;

            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5d;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append("[UsefulToolkit.MeshCut] 計測結果: ").AppendLine(Title);

            for (int i = 0; i < Infos.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(Infos[i].Label).Append(' ').Append(Infos[i].Value.ToString("N0"));
            }

            sb.AppendLine();
            sb.AppendLine(
                $"合計: 経過 {ElapsedMs:F2} ms ({ElapsedFrames} フレーム) / メイン実行 {MainExecuteMs:F2} ms / " +
                $"ワーカー実行 {WorkerExecuteMs:F2} ms / 何も実行していない時間 {IdleMs:F2} ms");
            sb.AppendLine("段階 | 種別 | 待ち ms | 実行 ms | 検知遅れ ms | フレーム");

            foreach (MeshCutStageRecord stage in Stages)
            {
                if (stage.IsBreakdown)
                {
                    sb.AppendLine(
                        $"  └ {stage.Name} | {KindLabel(stage.Kind)} | - | {stage.ExecuteMs:F2} | - | +{stage.Frame}");
                    continue;
                }

                // Frame が負の段階は、メインスレッドが完了を直接待たずに次のJobへ繋いだ段階
                string detect = stage.Frame < 0 ? "-" : stage.DetectDelayMs.ToString("F2");
                string frame = stage.Frame < 0 ? "-" : "+" + stage.Frame;

                sb.AppendLine(
                    $"{stage.Name} | {KindLabel(stage.Kind)} | {stage.WaitMs:F2} | {stage.ExecuteMs:F2} | " +
                    $"{detect} | {frame}");
            }

            sb.AppendLine(
                "待ち: 要求してから実行が始まるまで(Job同士を繋いだ段階は前の段階の終了から) / " +
                "検知遅れ: 実行が終わってからメインスレッドが完了に気付くまで / " +
                "フレーム: 計測開始から数えたフレーム数 / " +
                "-: メインスレッドが待たずに次のJobへ繋いだ段階 / └: 直前の段階の内訳(実行時間のみ)");

            return sb.ToString();
        }

        private static string KindLabel(MeshCutStageKind kind)
        {
            return kind switch
            {
                MeshCutStageKind.Main => "Main",
                MeshCutStageKind.Job => "Job",
                MeshCutStageKind.Background => "BG",
                _ => kind.ToString()
            };
        }
    }
}
