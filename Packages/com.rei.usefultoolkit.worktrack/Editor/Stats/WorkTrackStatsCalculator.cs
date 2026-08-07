using System;
using System.Collections.Generic;
using System.Linq;

namespace UsefulToolkit.WorkTrack
{
    /// <summary>
    /// 集計結果。日をまたぐセッションはローカル日付単位で按分してから日別/週別/月別集計を積み上げる。
    /// </summary>
    public class WorkTrackStats
    {
        public int SessionCount;
        public int WorkingDayCount;
        public TimeSpan TotalDuration;
        public TimeSpan AveragePerDay;
        public TimeSpan AveragePerSession;
        public TimeSpan MaxPerDay;
        public DateTime MaxPerDayDate;
        public TimeSpan MaxPerSession;
        public WorkSession MaxSession;
        public List<(string ProjectName, TimeSpan Duration)> PerProject = new();
        public List<(DateTime Day, TimeSpan Duration)> PerDay = new();
        public List<(DateTime WeekStart, TimeSpan Duration)> PerWeek = new();
        public List<(int Year, int Month, TimeSpan Duration)> PerMonth = new();
    }

    public static class WorkTrackStatsCalculator
    {
        /// <summary>
        /// periodStart/periodEndはローカル時刻。nullの場合はその方向に無制限。
        /// excludeOverlapがtrueの場合、複数プロジェクトを同時に開いていた等で発生する時間帯の重複を
        /// 除いた実時間を累計/日別/週別/月別集計に用いる(1セッションあたり・プロジェクト別は各セッション
        /// 自身の作業時間をそのまま表すべきものなので、この重複除去の影響を受けない)。
        /// </summary>
        public static WorkTrackStats Calculate(IReadOnlyList<WorkSession> sessions, DateTime? periodStart,
            DateTime? periodEnd, bool excludeOverlap = false)
        {
            var stats = new WorkTrackStats();

            var projectBuckets = new Dictionary<string, TimeSpan>();
            var clippedRanges = new List<(DateTime Start, DateTime End)>();
            var sessionDurationSum = TimeSpan.Zero;
            var maxSessionDuration = TimeSpan.Zero;
            WorkSession maxSession = null;
            int sessionCount = 0;

            foreach (var session in sessions)
            {
                if (!session.TryGetLocalRange(out var start, out var end)) continue;

                if (periodStart.HasValue && start < periodStart.Value) start = periodStart.Value;
                if (periodEnd.HasValue && end > periodEnd.Value) end = periodEnd.Value;
                if (end <= start) continue;

                sessionCount++;
                var duration = end - start;
                sessionDurationSum += duration;

                if (duration > maxSessionDuration)
                {
                    maxSessionDuration = duration;
                    maxSession = session;
                }

                projectBuckets.TryGetValue(session.ProjectName, out var projTotal);
                projectBuckets[session.ProjectName] = projTotal + duration;

                clippedRanges.Add((start, end));
            }

            stats.SessionCount = sessionCount;
            stats.MaxPerSession = maxSessionDuration;
            stats.MaxSession = maxSession;
            stats.AveragePerSession =
                sessionCount > 0 ? TimeSpan.FromTicks(sessionDurationSum.Ticks / sessionCount) : TimeSpan.Zero;
            stats.PerProject = projectBuckets.OrderByDescending(kv => kv.Value)
                .Select(kv => (kv.Key, kv.Value)).ToList();

            var effectiveRanges = excludeOverlap ? MergeRanges(clippedRanges) : clippedRanges;

            var dayBuckets = new Dictionary<DateTime, TimeSpan>();
            var total = TimeSpan.Zero;
            foreach (var (start, end) in effectiveRanges)
            {
                total += end - start;

                foreach (var (day, dayDuration) in SplitByLocalDay(start, end))
                {
                    dayBuckets.TryGetValue(day, out var dayTotal);
                    dayBuckets[day] = dayTotal + dayDuration;
                }
            }

            stats.TotalDuration = total;
            stats.WorkingDayCount = dayBuckets.Count;
            stats.AveragePerDay =
                dayBuckets.Count > 0 ? TimeSpan.FromTicks(total.Ticks / dayBuckets.Count) : TimeSpan.Zero;

            foreach (var kv in dayBuckets)
            {
                if (kv.Value <= stats.MaxPerDay) continue;
                stats.MaxPerDay = kv.Value;
                stats.MaxPerDayDate = kv.Key;
            }

            stats.PerDay = dayBuckets.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();

            var weekBuckets = new Dictionary<DateTime, TimeSpan>();
            foreach (var kv in dayBuckets)
            {
                var weekStart = GetWeekStart(kv.Key);
                weekBuckets.TryGetValue(weekStart, out var weekTotal);
                weekBuckets[weekStart] = weekTotal + kv.Value;
            }

            stats.PerWeek = weekBuckets.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();

            var monthBuckets = new Dictionary<(int Year, int Month), TimeSpan>();
            foreach (var kv in dayBuckets)
            {
                var key = (kv.Key.Year, kv.Key.Month);
                monthBuckets.TryGetValue(key, out var monthTotal);
                monthBuckets[key] = monthTotal + kv.Value;
            }

            stats.PerMonth = monthBuckets.OrderBy(kv => kv.Key)
                .Select(kv => (kv.Key.Year, kv.Key.Month, kv.Value)).ToList();

            return stats;
        }

        /// <summary>
        /// 「今日から遡って何日連続で作業しているか」。期間フィルタの影響を受けないよう、
        /// 呼び出し側は常に全履歴(フィルタ前のセッション一覧)を渡すこと。
        /// 今日まだ記録が無くても、昨日までの連続記録は途切れさせない(今日はまだ終わっていないため)。
        /// </summary>
        public static int CalculateConsecutiveWorkingDays(IReadOnlyList<WorkSession> allSessions)
        {
            var workingDays = new HashSet<DateTime>();
            foreach (var session in allSessions)
            {
                if (!session.TryGetLocalRange(out var start, out var end)) continue;
                foreach (var (day, _) in SplitByLocalDay(start, end))
                {
                    workingDays.Add(day);
                }
            }

            if (workingDays.Count == 0) return 0;

            var today = DateTime.Now.Date;
            var cursor = workingDays.Contains(today) ? today : today.AddDays(-1);

            int streak = 0;
            while (workingDays.Contains(cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        /// <summary>
        /// 時間帯が重なる/隣接する区間同士を1つにまとめ、実際に稼働していた時間帯の和集合を求める。
        /// </summary>
        private static List<(DateTime Start, DateTime End)> MergeRanges(List<(DateTime Start, DateTime End)> ranges)
        {
            if (ranges.Count <= 1) return ranges;

            var sorted = ranges.OrderBy(r => r.Start).ToList();
            var merged = new List<(DateTime Start, DateTime End)> { sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                var current = merged[^1];
                var next = sorted[i];

                if (next.Start <= current.End)
                {
                    if (next.End > current.End) merged[^1] = (current.Start, next.End);
                }
                else
                {
                    merged.Add(next);
                }
            }

            return merged;
        }

        private static IEnumerable<(DateTime Day, TimeSpan Duration)> SplitByLocalDay(DateTime start, DateTime end)
        {
            var cursor = start;
            while (cursor < end)
            {
                var dayEnd = cursor.Date.AddDays(1);
                var chunkEnd = end < dayEnd ? end : dayEnd;
                yield return (cursor.Date, chunkEnd - cursor);
                cursor = chunkEnd;
            }
        }

        private static DateTime GetWeekStart(DateTime day) => day.AddDays(-(int)day.DayOfWeek);
    }
}
