#nullable enable

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.ProjectStructure
{
    /// <summary>
    /// Assets以下をテンプレート通りの構造へ整理するウィンドウ。
    /// 必ずプレビューで内容を確認してから適用する流れにしている
    /// </summary>
    public class ProjectStructureWindow : EditorWindow
    {
        private ProjectStructureTemplate? _template;
        private string _templateSource = string.Empty;
        private string? _templateError;

        private ProjectStructurePlan? _plan;
        private ProjectStructureResult? _result;
        private List<string> _snapshotWarnings = new();

        private bool _includeFileRules;
        private Vector2 _scrollPosition;

        [MenuItem("UsefulToolkit/Project Structure", false, 17)]
        public static void ShowWindow()
        {
            var window = GetWindow<ProjectStructureWindow>("Project Structure");
            window.minSize = new Vector2(560, 480);
        }

        private void OnEnable()
        {
            ReloadTemplate();
        }

        private void OnGUI()
        {
            Rect area = new Rect(15, 15, position.width - 30, position.height - 30);
            GUILayout.BeginArea(area);

            DrawHeader();
            GUILayout.Space(10);

            DrawTemplateSection();
            GUILayout.Space(10);

            DrawSnapshotSection();
            GUILayout.Space(10);

            DrawExecuteSection();
            GUILayout.Space(10);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUI.skin.box);
            DrawDetail();
            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
            };
            EditorGUILayout.LabelField("Project Structure", titleStyle, GUILayout.Height(25));

            var descriptionStyle = new GUIStyle(EditorStyles.miniLabel)
                { wordWrap = true, normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField("テンプレートに従ってAssets以下を整理します。移動はAssetDatabase経由なので、参照は維持されます。",
                descriptionStyle);

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private void DrawTemplateSection()
        {
            EditorGUILayout.LabelField("テンプレート", EditorStyles.boldLabel);

            if (_templateError != null)
            {
                EditorGUILayout.HelpBox(_templateError, MessageType.Error);
            }
            else
            {
                string origin = ProjectStructureTemplateIO.HasProjectTemplate
                    ? "このプロジェクト専用の定義"
                    : "パッケージ同梱の既定テンプレート";
                EditorGUILayout.HelpBox($"{origin}\n{_templateSource}", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("再読み込み", EditorStyles.miniButtonLeft))
                {
                    ReloadTemplate();
                }

                if (GUILayout.Button("JSONを開く", EditorStyles.miniButtonMid))
                {
                    ProjectStructureTemplateIO.OpenInEditor(_templateSource);
                }

                EditorGUI.BeginDisabledGroup(ProjectStructureTemplateIO.HasProjectTemplate);
                {
                    if (GUILayout.Button("このプロジェクト用に複製", EditorStyles.miniButtonRight))
                    {
                        if (ProjectStructureTemplateIO.CopyDefaultToProject(out string? error))
                        {
                            AssetDatabase.Refresh();
                            ReloadTemplate();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Useful Toolkit", error ?? "複製に失敗しました。", "OK");
                        }
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSnapshotSection()
        {
            EditorGUILayout.LabelField("現在の構造を保存", EditorStyles.boldLabel);

            var noteStyle = new GUIStyle(EditorStyles.miniLabel)
                { wordWrap = true, normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField(
                "今のAssets以下をテンプレートとして書き出します。フォルダ構成とName照合ルールは作り直され、"
                + "手書きのExactPath / Folder / Globルールはそのまま引き継がれます。",
                noteStyle);

            _includeFileRules = EditorGUILayout.ToggleLeft(
                "ファイル単位のルールも生成する（親フォルダごと移動できないファイルのみ）", _includeFileRules);

            EditorGUI.BeginDisabledGroup(_template == null);
            {
                if (GUILayout.Button("現在の構造をテンプレートとして保存"))
                {
                    CaptureSnapshot();
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawExecuteSection()
        {
            EditorGUILayout.LabelField("整理の実行", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginDisabledGroup(_template == null);
                {
                    if (GUILayout.Button("プレビュー", GUILayout.Height(32)))
                    {
                        BuildPlan();
                    }
                }
                EditorGUI.EndDisabledGroup();

                bool canApply = _plan != null && _plan.TemplateErrors.Count == 0 && _plan.HasWork;
                EditorGUI.BeginDisabledGroup(!canApply);
                {
                    if (GUILayout.Button("適用", GUILayout.Height(32)))
                    {
                        ApplyPlan();
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (_plan == null)
            {
                EditorGUILayout.LabelField("まずプレビューを実行してください。", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                $"作成: {_plan.CreateCount}　移動: {_plan.MoveCount}　削除: {_plan.DeleteCount}　"
                + $"要確認: {_plan.SkipCount}　整理済み: {_plan.AlreadyInPlaceCount}",
                EditorStyles.miniLabel);
        }

        private void DrawDetail()
        {
            foreach (string warning in _snapshotWarnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            if (_result != null)
            {
                EditorGUILayout.LabelField("実行結果", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_result.Summarize(), EditorStyles.miniLabel);

                foreach (string error in _result.Errors)
                {
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                }

                GUILayout.Space(8);
            }

            if (_plan == null) return;

            foreach (string error in _plan.TemplateErrors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (_plan.TemplateErrors.Count > 0) return;

            if (!_plan.HasWork && _plan.SkipCount == 0)
            {
                EditorGUILayout.LabelField("整理の必要はありません。", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField("実行内容", EditorStyles.boldLabel);

            foreach (var operation in _plan.Operations)
            {
                DrawOperation(operation);
            }
        }

        private static void DrawOperation(ProjectStructureOperation operation)
        {
            (string label, Color color) = operation.Type switch
            {
                StructureOperationType.CreateFolder => ("作成", new Color(0.45f, 0.75f, 0.45f)),
                StructureOperationType.Move => ("移動", new Color(0.45f, 0.65f, 0.95f)),
                StructureOperationType.Delete => ("削除", new Color(0.95f, 0.55f, 0.45f)),
                _ => ("要確認", new Color(0.95f, 0.8f, 0.35f)),
            };

            EditorGUILayout.BeginHorizontal();
            {
                var labelStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = color } };
                EditorGUILayout.LabelField(label, labelStyle, GUILayout.Width(48));

                string body = operation.Type switch
                {
                    StructureOperationType.CreateFolder => operation.DestinationPath,
                    StructureOperationType.Move => $"{operation.SourcePath}  →  {operation.DestinationPath}",
                    StructureOperationType.Delete => operation.SourcePath,
                    _ => operation.SourcePath,
                };

                var bodyStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
                EditorGUILayout.LabelField(body, bodyStyle);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(operation.Reason))
            {
                var reasonStyle = new GUIStyle(EditorStyles.miniLabel)
                    { wordWrap = true, normal = { textColor = Color.gray } };
                EditorGUILayout.LabelField("        " + operation.Reason, reasonStyle);
            }

            Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
        }

        private void ReloadTemplate()
        {
            _template = ProjectStructureTemplateIO.Load(out _templateSource, out _templateError);
            _plan = null;
            _result = null;
            _snapshotWarnings.Clear();
        }

        private void BuildPlan()
        {
            _result = null;
            _plan = ProjectStructurePlanner.Build(_template);
        }

        private void CaptureSnapshot()
        {
            var captured = ProjectStructureSnapshot.Capture(_template, _includeFileRules, out var warnings);

            string message = $"現在の構造を以下へ保存します。\n\n{ProjectStructureTemplateIO.ProjectTemplatePath}\n\n"
                             + $"フォルダ: {captured.folders.Count} 件 / ルール: {captured.rules.Count} 件";

            if (ProjectStructureTemplateIO.HasProjectTemplate)
            {
                message += "\n\n既存のファイルは上書きされます。";
            }

            if (!EditorUtility.DisplayDialog("Useful Toolkit", message, "保存する", "やめる")) return;

            if (!ProjectStructureTemplateIO.SaveProjectTemplate(captured, out string? error))
            {
                EditorUtility.DisplayDialog("Useful Toolkit", error ?? "保存に失敗しました。", "OK");
                return;
            }

            Debug.Log($"[UsefulToolkit] 構造テンプレートを保存しました: {ProjectStructureTemplateIO.ProjectTemplatePath}");

            // 保存したテンプレートを読み直す。警告はその後に入れないとクリアされてしまう
            ReloadTemplate();
            _snapshotWarnings = warnings;
        }

        private void ApplyPlan()
        {
            // プレビュー後にAssetsが変わっている可能性があるので、必ず作り直してから実行する
            var plan = ProjectStructurePlanner.Build(_template);
            _plan = plan;

            if (plan.TemplateErrors.Count > 0)
            {
                EditorUtility.DisplayDialog("Useful Toolkit", "テンプレートに誤りがあるため実行できません。内容を確認してください。", "OK");
                return;
            }

            if (!plan.HasWork)
            {
                EditorUtility.DisplayDialog("Useful Toolkit", "整理の必要はありませんでした。", "OK");
                return;
            }

            string message = "以下を実行します。\n\n"
                             + $"フォルダ作成: {plan.CreateCount}\n"
                             + $"移動: {plan.MoveCount}\n"
                             + $"削除: {plan.DeleteCount}\n\n";

            if (plan.DeleteCount > 0)
            {
                message += "削除対象はOSのゴミ箱へ送られます（完全削除はしません）。\n\n";
            }

            if (plan.SkipCount > 0)
            {
                message += $"要確認が {plan.SkipCount} 件あります。これらは実行されません。\n\n";
            }

            message += "よろしいですか？";

            if (!EditorUtility.DisplayDialog("Useful Toolkit", message, "実行する", "やめる")) return;

            _result = ProjectStructureApplier.Apply(plan);

            foreach (string log in _result.Logs)
            {
                Debug.Log($"[UsefulToolkit] {log}");
            }

            foreach (string error in _result.Errors)
            {
                Debug.LogError($"[UsefulToolkit] {error}");
            }

            Debug.Log($"[UsefulToolkit] 整理が完了しました。{_result.Summarize()}");

            // 実行後の状態で作り直して、残った要確認項目を表示する
            _plan = ProjectStructurePlanner.Build(_template);
        }
    }
}
