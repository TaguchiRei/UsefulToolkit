using System;
using System.Diagnostics;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// 記録中のセッションと、それを記録しているUnityプロセスの組。CurrentSessions/{SessionId}.jsonに1件ずつ保存する。
    /// プロセスIDは再利用されるため、プロセスの起動時刻と合わせて持ち主を特定する。
    /// </summary>
    [Serializable]
    internal class CurrentSessionRecord
    {
        public WorkSession Session;
        public int ProcessId;

        /// <summary> 記録しているプロセスの起動時刻(UTC)のTicks </summary>
        public long ProcessStartTicks;

        public static CurrentSessionRecord CreateForCurrentProcess(WorkSession session)
        {
            using var process = Process.GetCurrentProcess();
            return new CurrentSessionRecord
            {
                Session = session,
                ProcessId = process.Id,
                ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks
            };
        }

        /// <summary>
        /// 記録しているプロセスが動いているかを返す。
        /// 確かめられないときは動いているものとして扱い、他のUnityが記録中のセッションを確定させない。
        /// </summary>
        public bool IsOwnerAlive()
        {
            try
            {
                using var process = Process.GetProcessById(ProcessId);
                if (process.HasExited) return false;

                var startTicks = process.StartTime.ToUniversalTime().Ticks;
                return Math.Abs(startTicks - ProcessStartTicks) < TimeSpan.TicksPerSecond;
            }
            catch (ArgumentException)
            {
                // 該当するIDのプロセスがない
                return false;
            }
            catch (InvalidOperationException)
            {
                // 取得してから調べるまでの間にプロセスが終了した
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
