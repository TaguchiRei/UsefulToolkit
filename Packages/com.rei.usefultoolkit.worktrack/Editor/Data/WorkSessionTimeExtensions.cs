using System;
using System.Globalization;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// WorkSessionの開始/終了時刻をローカル時刻のDateTimeとして取得する共通ヘルパー。
    /// 終了前(進行中)のセッションは現在時刻を仮の終了時刻として扱う。
    /// </summary>
    internal static class WorkSessionTimeExtensions
    {
        public static bool TryGetLocalRange(this WorkSession session, out DateTime start, out DateTime end)
        {
            start = default;
            end = default;

            if (!DateTime.TryParse(session.StartTime, null, DateTimeStyles.RoundtripKind, out var startUtc))
                return false;

            DateTime endUtc;
            if (string.IsNullOrEmpty(session.EndTime))
            {
                endUtc = DateTime.UtcNow;
            }
            else if (!DateTime.TryParse(session.EndTime, null, DateTimeStyles.RoundtripKind, out endUtc))
            {
                return false;
            }

            start = startUtc.ToLocalTime();
            end = endUtc.ToLocalTime();
            return end > start;
        }
    }
}
