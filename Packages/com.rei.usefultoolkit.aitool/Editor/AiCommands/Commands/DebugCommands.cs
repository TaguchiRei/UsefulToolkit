using System;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class GetConsoleLogsCommand : IGetCommand
    {
        public string Description => "【コンソールログ取得】直近のコンソールログを取得します。引数(配列): [\"maxCount\"]";

        public string Execute(string argument)
        {
            int maxCount = 10;
            try
            {
                string[] args = JsonConvert.DeserializeObject<string[]>(argument);
                if (args != null && args.Length > 0) int.TryParse(args[0], out maxCount);
            }
            catch { }

            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
                if (logEntriesType == null) return "Error: Could not find UnityEditor.LogEntries type.";

                var getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
                
                int count = (int)getCountMethod.Invoke(null, null);
                int start = Math.Max(0, count - maxCount);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Total Logs: {count}. Showing last {Math.Min(count, maxCount)} logs:");

                var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor.dll");
                object logEntry = Activator.CreateInstance(logEntryType);
                var conditionField = logEntryType.GetField("condition", BindingFlags.Instance | BindingFlags.Public);
                var modeField = logEntryType.GetField("mode", BindingFlags.Instance | BindingFlags.Public);

                for (int i = start; i < count; i++)
                {
                    getEntryMethod.Invoke(null, new object[] { i, logEntry });
                    string condition = (string)conditionField.GetValue(logEntry);
                    int mode = (int)modeField.GetValue(logEntry);

                    string typeStr = GetLogType(mode);
                    sb.AppendLine($"[{typeStr}] {condition}");
                }

                return sb.ToString();
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private string GetLogType(int mode)
        {
            if ((mode & 0x400) != 0) return "Error";
            if ((mode & 0x800) != 0) return "Assert";
            if ((mode & 0x1000) != 0) return "Warning";
            if ((mode & 0x2000) != 0) return "Log";
            if ((mode & 0x4000) != 0) return "Exception";
            return "Unknown";
        }
    }
}
