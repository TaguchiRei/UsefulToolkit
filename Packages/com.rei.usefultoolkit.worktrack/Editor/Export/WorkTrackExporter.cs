using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// セッション一覧をCSV/JSON形式のテキストに変換する。
    /// </summary>
    public static class WorkTrackExporter
    {
        public static string ExportCsv(IEnumerable<WorkSession> sessions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ProjectName,StartTime,EndTime,DurationSeconds,Memo,Tags");

            foreach (var session in sessions)
            {
                var durationSeconds = session.TryGetLocalRange(out var start, out var end)
                    ? (end - start).TotalSeconds
                    : 0d;

                sb.AppendLine(string.Join(",",
                    EscapeCsv(session.ProjectName),
                    session.StartTime,
                    session.EndTime,
                    durationSeconds.ToString("0", CultureInfo.InvariantCulture),
                    EscapeCsv(session.Memo),
                    EscapeCsv(session.Tags != null ? string.Join(";", session.Tags) : "")));
            }

            return sb.ToString();
        }

        public static string ExportJson(IEnumerable<WorkSession> sessions)
        {
            var wrapper = new WorkSessionListData { Sessions = sessions.ToList() };
            return JsonUtility.ToJson(wrapper, true);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
