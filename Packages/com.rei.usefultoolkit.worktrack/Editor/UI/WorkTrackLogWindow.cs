using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// 記録済みの作業セッション履歴の閲覧・編集・削除・集計・エクスポートを行うウィンドウ。
    /// </summary>
    public class WorkTrackLogWindow : EditorWindow
    {
        private enum ViewMode
        {
            History,
            Stats
        }

        private enum PeriodPreset
        {
            Today,
            ThisWeek,
            ThisMonth,
            All,
            Custom
        }

        [MenuItem("UsefulToolkit/WorkTrack/Log Viewer")]
        public static void Open()
        {
            var window = GetWindow<WorkTrackLogWindow>("WorkTrack");
            window.minSize = new Vector2(480, 400);
        }

        private static readonly Color InProgressBackgroundColor = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color SelectedPeriodColor = new Color(0.3f, 0.6f, 0.9f);
        private static readonly Color ProjectBarBackgroundColor = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color ProjectBarFillColor = new Color(0.3f, 0.6f, 0.9f);
        private static readonly string[] BulkDeleteUnitLabels = { "日", "ヶ月", "年" };

        // Sessions.jsonの内容(編集・削除対象)。表示用の合成前の生データ。
        private List<WorkSession> _finalizedSessions = new();

        // CurrentSession.jsonの内容(表示専用、編集・削除の対象外)。
        private WorkSession _currentSession;

        // 履歴/集計タブの表示用に、確定履歴と進行中セッションを合成したもの。
        private List<WorkSession> _displaySessions = new();

        private Vector2 _historyScrollPosition;
        private Vector2 _statsScrollPosition;

        private ViewMode _viewMode = ViewMode.History;
        private PeriodPreset _periodPreset = PeriodPreset.All;
        private string _customStartText = "";
        private string _customEndText = "";

        private WorkTrackStats _stats;
        private int _consecutiveWorkingDays;
        private bool _includeOverlap = true;

        private string _editingSessionId;
        private string _editMemoBuffer;
        private string _editTagsBuffer;

        private int _bulkDeleteAmount = 3;
        private int _bulkDeleteUnitIndex = 1; // ヶ月

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            _finalizedSessions = WorkTrackRepository.LoadSessions();
            _finalizedSessions.Sort((a, b) => string.CompareOrdinal(b.StartTime, a.StartTime));

            _currentSession = WorkTrackRepository.LoadCurrentSession();

            _displaySessions = new List<WorkSession>(_finalizedSessions);
            if (_currentSession != null) _displaySessions.Insert(0, _currentSession);

            _editingSessionId = null;

            _consecutiveWorkingDays = WorkTrackStatsCalculator.CalculateConsecutiveWorkingDays(_displaySessions);
            RecalculateStats();
        }

        private void RecalculateStats()
        {
            var (start, end) = GetPeriodRange();
            _stats = WorkTrackStatsCalculator.Calculate(_displaySessions, start, end, !_includeOverlap);
        }

        private (DateTime? Start, DateTime? End) GetPeriodRange()
        {
            var now = DateTime.Now;

            switch (_periodPreset)
            {
                case PeriodPreset.Today:
                    return (now.Date, now.Date.AddDays(1));
                case PeriodPreset.ThisWeek:
                    var weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
                    return (weekStart, weekStart.AddDays(7));
                case PeriodPreset.ThisMonth:
                    var monthStart = new DateTime(now.Year, now.Month, 1);
                    return (monthStart, monthStart.AddMonths(1));
                case PeriodPreset.Custom:
                    DateTime? start = DateTime.TryParse(_customStartText, out var startDate)
                        ? startDate.Date
                        : null;
                    DateTime? end = DateTime.TryParse(_customEndText, out var endDate)
                        ? endDate.Date.AddDays(1)
                        : null;
                    return (start, end);
                default:
                    return (null, null);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _viewMode = (ViewMode)GUILayout.Toolbar((int)_viewMode, new[] { "履歴", "集計" },
                    EditorStyles.toolbarButton, GUILayout.Width(160));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("更新", EditorStyles.toolbarButton)) Refresh();
            }

            if (_viewMode == ViewMode.History) DrawHistoryView();
            else DrawStatsView();
        }

        //  履歴タブ

        private void DrawHistoryView()
        {
            DrawBulkDeleteSection();
            EditorGUILayout.Space();

            if (_displaySessions.Count == 0)
            {
                EditorGUILayout.HelpBox("記録されたセッションがありません。", MessageType.Info);
                return;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_historyScrollPosition))
            {
                _historyScrollPosition = scroll.scrollPosition;

                foreach (var session in _displaySessions)
                {
                    DrawSessionRow(session);
                }
            }
        }

        private void DrawBulkDeleteSection()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("一括削除:", GUILayout.Width(60));
                _bulkDeleteAmount = Mathf.Max(1, EditorGUILayout.IntField(_bulkDeleteAmount, GUILayout.Width(40)));
                _bulkDeleteUnitIndex = EditorGUILayout.Popup(_bulkDeleteUnitIndex, BulkDeleteUnitLabels,
                    GUILayout.Width(60));
                EditorGUILayout.LabelField("以上前のセッションを削除", GUILayout.Width(150));

                if (GUILayout.Button("実行", GUILayout.Width(60))) ExecuteBulkDelete();
            }
        }

        private DateTime GetBulkDeleteThreshold()
        {
            var now = DateTime.Now;
            return _bulkDeleteUnitIndex switch
            {
                0 => now.AddDays(-_bulkDeleteAmount),
                1 => now.AddMonths(-_bulkDeleteAmount),
                2 => now.AddYears(-_bulkDeleteAmount),
                _ => now
            };
        }

        private void ExecuteBulkDelete()
        {
            var threshold = GetBulkDeleteThreshold();

            var targets = _finalizedSessions.Where(s =>
                DateTime.TryParse(s.StartTime, null, DateTimeStyles.RoundtripKind, out var startUtc) &&
                startUtc.ToLocalTime() < threshold).ToList();

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("一括削除", "対象のセッションはありません。", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("一括削除",
                $"{threshold:yyyy/MM/dd}より前の{targets.Count}件のセッションを削除します。この操作は取り消せません。",
                "削除", "キャンセル");
            if (!confirmed) return;

            var targetIds = new HashSet<string>(targets.Select(t => t.SessionId));
            _finalizedSessions.RemoveAll(s => targetIds.Contains(s.SessionId));
            WorkTrackRepository.SaveSessions(_finalizedSessions);
            Refresh();
        }

        private void DrawSessionRow(WorkSession session)
        {
            bool isInProgress = string.IsNullOrEmpty(session.EndTime);
            bool isEditing = !isInProgress && _editingSessionId == session.SessionId;

            var previousColor = GUI.backgroundColor;
            if (isInProgress) GUI.backgroundColor = InProgressBackgroundColor;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // 背景色はhelpBoxの枠自体にのみ適用し、中身のコントロールには影響させない
                GUI.backgroundColor = previousColor;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(session.ProjectName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (!isInProgress && !isEditing)
                    {
                        if (GUILayout.Button("編集", GUILayout.Width(50))) BeginEdit(session);
                        if (GUILayout.Button("削除", GUILayout.Width(50))) DeleteSession(session);
                    }
                }

                var endLabel = isInProgress ? "記録中" : FormatTime(session.EndTime);
                EditorGUILayout.LabelField($"{FormatTime(session.StartTime)}  〜  {endLabel}");
                EditorGUILayout.LabelField($"作業時間: {FormatDuration(session)}");

                if (isEditing)
                {
                    EditorGUILayout.LabelField("メモ");
                    _editMemoBuffer = EditorGUILayout.TextArea(_editMemoBuffer, GUILayout.Height(40));
                    EditorGUILayout.LabelField("タグ (カンマ区切り)");
                    _editTagsBuffer = EditorGUILayout.TextField(_editTagsBuffer);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("キャンセル", GUILayout.Width(80))) CancelEdit();
                        if (GUILayout.Button("保存", GUILayout.Width(80))) SaveEdit(session);
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(session.Memo))
                    {
                        EditorGUILayout.LabelField($"メモ: {session.Memo}", EditorStyles.wordWrappedLabel);
                    }

                    if (session.Tags is { Length: > 0 })
                    {
                        EditorGUILayout.LabelField($"タグ: {string.Join(", ", session.Tags)}");
                    }
                }
            }
        }

        private void BeginEdit(WorkSession session)
        {
            _editingSessionId = session.SessionId;
            _editMemoBuffer = session.Memo ?? "";
            _editTagsBuffer = session.Tags != null ? string.Join(", ", session.Tags) : "";
        }

        private void CancelEdit() => _editingSessionId = null;

        private void SaveEdit(WorkSession session)
        {
            var target = _finalizedSessions.Find(s => s.SessionId == session.SessionId);
            if (target != null)
            {
                target.Memo = _editMemoBuffer;
                target.Tags = _editTagsBuffer
                    .Split(',')
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .ToArray();
                target.UpdatedAt = DateTime.UtcNow.ToString("o");

                WorkTrackRepository.SaveSessions(_finalizedSessions);
            }

            Refresh();
        }

        private void DeleteSession(WorkSession session)
        {
            bool confirmed = EditorUtility.DisplayDialog("セッションを削除",
                $"{session.ProjectName} のセッション({FormatTime(session.StartTime)})を削除しますか？この操作は取り消せません。",
                "削除", "キャンセル");
            if (!confirmed) return;

            _finalizedSessions.RemoveAll(s => s.SessionId == session.SessionId);
            WorkTrackRepository.SaveSessions(_finalizedSessions);
            Refresh();
        }

        //  集計タブ

        private void DrawStatsView()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPeriodButton("今日", PeriodPreset.Today);
                DrawPeriodButton("今週", PeriodPreset.ThisWeek);
                DrawPeriodButton("今月", PeriodPreset.ThisMonth);
                DrawPeriodButton("全期間", PeriodPreset.All);
                DrawPeriodButton("カスタム", PeriodPreset.Custom);
            }

            if (_periodPreset == PeriodPreset.Custom)
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("開始日", GUILayout.Width(40));
                        _customStartText = EditorGUILayout.TextField(_customStartText, GUILayout.Width(100));
                        EditorGUILayout.LabelField("終了日", GUILayout.Width(40));
                        _customEndText = EditorGUILayout.TextField(_customEndText, GUILayout.Width(100));
                    }

                    if (check.changed) RecalculateStats();
                }

                EditorGUILayout.HelpBox("日付は yyyy-MM-dd 形式で入力してください。", MessageType.None);
            }

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _includeOverlap = EditorGUILayout.ToggleLeft(
                    "重複時間を含める(複数プロジェクトを同時に開いていた時間帯をそれぞれ加算する)", _includeOverlap);
                if (check.changed) RecalculateStats();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("CSVエクスポート")) ExportCurrentPeriod(asCsv: true);
                if (GUILayout.Button("JSONエクスポート")) ExportCurrentPeriod(asCsv: false);
            }

            EditorGUILayout.Space();

            if (_stats == null || _stats.SessionCount == 0)
            {
                EditorGUILayout.HelpBox("この期間に記録されたセッションがありません。", MessageType.Info);
                return;
            }

            using (var scroll = new EditorGUILayout.ScrollViewScope(_statsScrollPosition))
            {
                _statsScrollPosition = scroll.scrollPosition;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("概要", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"累計作業時間: {FormatTimeSpan(_stats.TotalDuration)}");
                    EditorGUILayout.LabelField($"セッション数: {_stats.SessionCount}");
                    EditorGUILayout.LabelField($"稼働日数: {_stats.WorkingDayCount}");
                    EditorGUILayout.LabelField($"連続作業日数: {_consecutiveWorkingDays}日");
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("平均・最大", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"平均作業時間(1日あたり): {FormatTimeSpan(_stats.AveragePerDay)}");
                    EditorGUILayout.LabelField($"平均作業時間(1セッションあたり): {FormatTimeSpan(_stats.AveragePerSession)}");
                    EditorGUILayout.LabelField(
                        $"最大稼働時間(1日): {FormatTimeSpan(_stats.MaxPerDay)} ({_stats.MaxPerDayDate:yyyy/MM/dd})");
                    if (_stats.MaxSession != null)
                    {
                        EditorGUILayout.LabelField(
                            $"最大稼働時間(1セッション): {FormatTimeSpan(_stats.MaxPerSession)} ({_stats.MaxSession.ProjectName})");
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("プロジェクト別", EditorStyles.boldLabel);
                    foreach (var (projectName, duration) in _stats.PerProject)
                    {
                        var ratio = _stats.TotalDuration.Ticks > 0
                            ? (float)duration.Ticks / _stats.TotalDuration.Ticks
                            : 0f;

                        EditorGUILayout.LabelField($"{projectName}: {FormatTimeSpan(duration)} ({ratio * 100f:0.0}%)");
                        DrawRatioBar(ratio);
                        GUILayout.Space(4);
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("日別集計", EditorStyles.boldLabel);
                    foreach (var (day, duration) in _stats.PerDay)
                    {
                        EditorGUILayout.LabelField($"{day:yyyy/MM/dd (ddd)}: {FormatTimeSpan(duration)}");
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("週別集計", EditorStyles.boldLabel);
                    foreach (var (weekStart, duration) in _stats.PerWeek)
                    {
                        EditorGUILayout.LabelField($"{weekStart:yyyy/MM/dd}週: {FormatTimeSpan(duration)}");
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("月別集計", EditorStyles.boldLabel);
                    foreach (var (year, month, duration) in _stats.PerMonth)
                    {
                        EditorGUILayout.LabelField($"{year}年{month}月: {FormatTimeSpan(duration)}");
                    }
                }
            }
        }

        private void DrawPeriodButton(string label, PeriodPreset preset)
        {
            var isSelected = _periodPreset == preset;

            var previousColor = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = SelectedPeriodColor;

            if (GUILayout.Button(label, EditorStyles.miniButton))
            {
                _periodPreset = preset;
                RecalculateStats();
            }

            GUI.backgroundColor = previousColor;
        }

        private static void DrawRatioBar(float ratio)
        {
            var barRect = EditorGUILayout.GetControlRect(false, 6);
            EditorGUI.DrawRect(barRect, ProjectBarBackgroundColor);

            var fillRect = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(ratio), barRect.height);
            EditorGUI.DrawRect(fillRect, ProjectBarFillColor);
        }

        //  エクスポート

        private List<WorkSession> GetFinalizedSessionsInCurrentPeriod()
        {
            var (start, end) = GetPeriodRange();

            return _finalizedSessions.Where(s =>
            {
                if (!DateTime.TryParse(s.StartTime, null, DateTimeStyles.RoundtripKind, out var startUtc))
                    return false;

                var sessionStart = startUtc.ToLocalTime();
                if (start.HasValue && sessionStart < start.Value) return false;
                if (end.HasValue && sessionStart >= end.Value) return false;
                return true;
            }).ToList();
        }

        private void ExportCurrentPeriod(bool asCsv)
        {
            var sessions = GetFinalizedSessionsInCurrentPeriod();
            if (sessions.Count == 0)
            {
                EditorUtility.DisplayDialog("エクスポート", "対象期間に確定済みのセッションがありません。", "OK");
                return;
            }

            WorkTrackPaths.EnsureDirectories();

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = asCsv ? "csv" : "json";
            var path = Path.Combine(WorkTrackPaths.ExportDirectory, $"WorkTrack_{timestamp}.{extension}");
            var content = asCsv ? WorkTrackExporter.ExportCsv(sessions) : WorkTrackExporter.ExportJson(sessions);

            File.WriteAllText(path, content);

            EditorUtility.DisplayDialog("エクスポート完了", $"{sessions.Count}件のセッションを書き出しました。\n{path}", "OK");
            EditorUtility.RevealInFinder(path);
        }

        //  共通フォーマット

        private static string FormatTime(string iso)
        {
            return DateTime.TryParse(iso, null, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                : iso;
        }

        private static string FormatDuration(WorkSession session)
        {
            return session.TryGetLocalRange(out var start, out var end)
                ? FormatTimeSpan(end - start)
                : "-";
        }

        private static string FormatTimeSpan(TimeSpan duration)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }
}
