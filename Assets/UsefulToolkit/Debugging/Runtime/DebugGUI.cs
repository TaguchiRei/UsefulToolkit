using UnityEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace UsefulToolkit.Debugging
{
    using Debug = UnityEngine.Debug;

    public class DebugGUI : MonoBehaviour
    {
#if UNITY_EDITOR

        private Vector2 Position => new Vector2(UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.PosX", 10f),
            UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.PosY", 10f));

        private int FontSize => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.FontSize", 20);
        private int FPSSampling => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.FPSSampling", 10);

        private bool RemoveMissingReferences =>
            UnityEditor.EditorPrefs.GetBool("UsefulTools.Debug.RemoveMissing", true);

        private int MaxLogCount => UnityEditor.EditorPrefs.GetInt("UsefulTools.Debug.MaxLogCount", 10);
        private float LogTimeout => UnityEditor.EditorPrefs.GetFloat("UsefulTools.Debug.LogTimeout", 5.0f);

        private struct LogData
        {
            public string Message;
            public LogType Type;
            public float Time;
        }

#endif

        private readonly List<(string, Func<string>)> _getValueFunc = new();

        private static DebugGUI _instance;

        private GUIStyle _debugStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _logStyle;

        private Rect _rect;

#if UNITY_EDITOR

        private LogData[] _logs;
        private int _logStart;
        private int _logCount;

        private float[] _fpsSamples;
        private int _fpsIndex;
        private int _fpsCount;

        private readonly List<int> _removeIndexes = new();

        private readonly StringBuilder _stringBuilder = new(256);

        // スレッドセーフなログキュー
        private readonly System.Collections.Concurrent.ConcurrentQueue<LogData> _threadedLogQueue = new();

#endif

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;

#if UNITY_EDITOR
                InitializeStyles();
                InitializeBuffers();
                Application.logMessageReceivedThreaded += OnLogReceived;
#endif

                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            Application.logMessageReceivedThreaded -= OnLogReceived;
#endif
            _instance = null;
        }

#if UNITY_EDITOR

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            if (!UnityEditor.EditorPrefs.GetBool("UsefulTools.Debug.LogCaptureEnabled", false)) return;

            // メインスレッド以外からも呼ばれるためキューに積む
            _threadedLogQueue.Enqueue(new LogData
            {
                Message = condition,
                Type = type,
                Time = -1f // UpdateでTime.timeを代入
            });
        }

        private void InitializeStyles()
        {
            _debugStyle = new GUIStyle
            {
                fontSize = FontSize,
                normal = { textColor = Color.white }
            };

            _errorStyle = new GUIStyle
            {
                fontSize = FontSize,
                normal = { textColor = Color.red }
            };

            _logStyle = new GUIStyle
            {
                fontSize = FontSize,
                alignment = TextAnchor.UpperRight,
                normal = { textColor = Color.white }
            };
        }

        private void InitializeBuffers()
        {
            _logs = new LogData[MaxLogCount];
            _fpsSamples = new float[FPSSampling];
        }

        private void OnGUI()
        {
            if (_debugStyle == null || _debugStyle.fontSize != FontSize)
            {
                InitializeStyles();
            }

            if (_logs == null || _logs.Length != MaxLogCount)
            {
                InitializeBuffers();
                _logStart = 0;
                _logCount = 0;
            }

            if (!Mathf.Approximately(_rect.width, Screen.width) ||
                !Mathf.Approximately(_rect.height, Screen.height))
            {
                var pos = Position;
                _rect = new Rect(pos.x, pos.y, Screen.width, Screen.height);
            }

            var prevEnabled = GUI.enabled;
            var prevColor = GUI.color;

            GUI.enabled = false;
            GUI.color = Color.white;

            GUI.BeginGroup(_rect);
            GUILayout.BeginVertical();

            DrawFPS();
            DrawVariables();

            GUILayout.EndVertical();
            GUI.EndGroup();

            DrawLogs();

            GUI.enabled = prevEnabled;
            GUI.color = prevColor;
        }

        private void DrawFPS()
        {
            _stringBuilder.Clear();
            _stringBuilder.Append("FPS : ");
            _stringBuilder.Append((1f / Time.deltaTime).ToString("000.0"));
            GUILayout.Label(_stringBuilder.ToString(), _debugStyle);

            _stringBuilder.Clear();
            _stringBuilder.Append("Average FPS : ");
            _stringBuilder.Append(GetAverageFPS().ToString("000.0"));
            GUILayout.Label(_stringBuilder.ToString(), _debugStyle);
        }

        private void DrawVariables()
        {
            int index = 0;
            foreach (var item in _getValueFunc)
            {
                try
                {
                    _stringBuilder.Clear();
                    _stringBuilder.Append(item.Item1);
                    _stringBuilder.Append(" : ");
                    _stringBuilder.Append(item.Item2.Invoke());

                    GUILayout.Label(_stringBuilder.ToString(), _debugStyle);
                }
                catch (Exception)
                {
                    DrawMissing(item.Item1, index);
                }

                index++;
            }

            if (RemoveMissingReferences && _removeIndexes.Count > 0)
            {
                _removeIndexes.Sort();
                _removeIndexes.Reverse();
                foreach (var i in _removeIndexes)
                {
                    _getValueFunc.RemoveAt(i);
                }

                _removeIndexes.Clear();
            }
        }

        private void DrawMissing(string name, int index)
        {
            _stringBuilder.Clear();
            _stringBuilder.Append(name);
            _stringBuilder.Append(" : 値が見つかりません");

            GUILayout.Label(_stringBuilder.ToString(), _errorStyle);

            if (RemoveMissingReferences)
            {
                _removeIndexes.Add(index);
            }
        }

        private void DrawLogs()
        {
            float areaWidth = Screen.width * 0.5f;
            Rect logArea = new Rect(Screen.width - areaWidth - 10, 10, areaWidth, Screen.height - 20);

            GUILayout.BeginArea(logArea);
            GUILayout.BeginVertical();

            var prevContentColor = GUI.contentColor;

            for (int i = 0; i < _logCount; i++)
            {
                int index = (_logStart + _logCount - 1 - i + _logs.Length) % _logs.Length;
                var log = _logs[index];

                GUI.contentColor = GetLogColor(log.Type);
                GUILayout.Label(log.Message, _logStyle);
            }

            GUI.contentColor = prevContentColor;

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private Color GetLogColor(LogType type)
        {
            return type switch
            {
                LogType.Error or LogType.Exception or LogType.Assert => Color.red,
                LogType.Warning => Color.yellow,
                _ => Color.white
            };
        }

        private void Update()
        {
            ProcessThreadedLogs();
            UpdateFPS();
            UpdateLogs();
        }

        private void ProcessThreadedLogs()
        {
            while (_threadedLogQueue.TryDequeue(out var log))
            {
                AddLogInternal(log.Message, log.Type);
            }
        }

        private void UpdateFPS()
        {
            if (_fpsSamples == null || _fpsSamples.Length == 0) return;
            _fpsSamples[_fpsIndex] = Time.deltaTime;
            _fpsIndex = (_fpsIndex + 1) % _fpsSamples.Length;

            if (_fpsCount < _fpsSamples.Length)
            {
                _fpsCount++;
            }
        }

        private void UpdateLogs()
        {
            if (_logCount == 0) return;

            float current = Time.time;
            float timeout = LogTimeout;

            for (int i = 0; i < _logCount; i++)
            {
                int index = (_logStart + i) % _logs.Length;

                if (current - _logs[index].Time > timeout)
                {
                    _logStart = (_logStart + 1) % _logs.Length;
                    _logCount--;
                    i--;
                }
            }
        }

#endif

        [Conditional("UNITY_EDITOR")]
        public static void ObserveVariable(string name, Func<string> debugValue)
        {
#if UNITY_EDITOR
            if (_instance == null)
            {
                Debug.LogWarning("DebugGUIの初期化前に登録メソッドが呼ばれました");
                return;
            }

            _instance._getValueFunc.Add((name, debugValue));
#endif
        }

        [Conditional("UNITY_EDITOR")]
        public static void Log(string message)
        {
            AddLog(message, LogType.Log);
        }

        [Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message)
        {
            AddLog(message, LogType.Warning);
        }

        [Conditional("UNITY_EDITOR")]
        public static void LogError(string message)
        {
            AddLog(message, LogType.Error);
        }

        private static void AddLog(string message, LogType type)
        {
#if UNITY_EDITOR
            // コンソールに流す。購読している OnLogReceived がこれをキャッチして画面にも表示される
            switch (type)
            {
                case LogType.Log: UnityEngine.Debug.Log(message); break;
                case LogType.Warning: UnityEngine.Debug.LogWarning(message); break;
                case LogType.Error: UnityEngine.Debug.LogError(message); break;
            }
#endif
        }

        private void AddLogInternal(string message, LogType type)
        {
#if UNITY_EDITOR
            if (_logs == null || _logs.Length == 0) return;

            int index = (_logStart + _logCount) % _logs.Length;

            _logs[index].Message = message;
            _logs[index].Type = type;
            _logs[index].Time = Time.time;

            if (_logCount < _logs.Length)
            {
                _logCount++;
            }
            else
            {
                _logStart = (_logStart + 1) % _logs.Length;
            }
#endif
        }

        private float GetAverageFPS()
        {
#if UNITY_EDITOR
            if (_fpsCount == 0) return 0f;

            float sum = 0;
            for (int i = 0; i < _fpsCount; i++)
            {
                sum += _fpsSamples[i];
            }

            return 1f / (sum / _fpsCount);
#else
        return 0f;
#endif
        }
    }
}