using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UsefulToolkit.Ai
{
    /// <summary>
    /// hatayama/unity-cli-loop（旧uLoopMCP）の導入を自動化するツール。
    /// UPMパッケージの追加、CLI(uloop-cli)のグローバルインストール、Skillsの導入までを画面上から実行できる。
    /// https://github.com/hatayama/unity-cli-loop
    /// </summary>
    public class UnityCliLoopInstaller : EditorWindow
    {
        private const string RepositoryUrl = "https://github.com/hatayama/unity-cli-loop";
        private const string PackageGitUrl = "https://github.com/hatayama/unity-cli-loop.git?path=/Packages/src";
        private const string PackageId = "io.github.hatayama.uloopmcp";
        private const string SettingsMenuPath = "Window/Unity CLI Loop/Settings";

        private enum SkillsTarget
        {
            Claude,
            Codex
        }

        private static AddRequest _addRequest;
        private static ListRequest _listRequest;
        private static bool? _isPackageInstalled;

        private static Process _runningProcess;
        private static readonly StringBuilder ProcessOutput = new();
        private static string _lastCommandLabel;

        private Vector2 _scrollPosition;
        private Vector2 _logScrollPosition;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installGlobalSkills;

        [MenuItem("UsefulToolkit/AI/Unity CLI Loop Installer", false, 13)]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityCliLoopInstaller>("Unity CLI Loop Installer");
            window.minSize = new Vector2(480, 480);
            RefreshInstalledState();
        }

        private void OnEnable()
        {
            if (_isPackageInstalled == null && _listRequest == null)
            {
                RefreshInstalledState();
            }
        }

        private void OnGUI()
        {
            Rect area = new Rect(15, 15, position.width - 30, position.height - 30);
            GUILayout.BeginArea(area);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            GUILayout.Space(10);

            bool isBusy = IsBusy();
            EditorGUI.BeginDisabledGroup(isBusy);

            DrawStepInstallPackage();
            GUILayout.Space(8);
            DrawStepCheckNode();
            GUILayout.Space(8);
            DrawStepInstallCli();
            GUILayout.Space(8);
            DrawStepInstallSkills();
            GUILayout.Space(8);
            DrawStepOpenSettings();

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);
            DrawLog(isBusy);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            EditorGUILayout.LabelField("Unity CLI Loop Installer", titleStyle);
            EditorGUILayout.LabelField(
                "AIエージェント(Claude Code等)からUnity Editorを直接操作できるようにする hatayama/unity-cli-loop（旧uLoopMCP）の導入を自動化します。",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var repoStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray } };
                EditorGUILayout.LabelField($"Src: {RepositoryUrl}", repoStyle);
                if (GUILayout.Button("開く", GUILayout.Width(50)))
                {
                    Application.OpenURL(RepositoryUrl);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string statusText = _isPackageInstalled switch
                {
                    null => "パッケージ状態: 確認中...",
                    true => "パッケージ状態: インストール済み",
                    false => "パッケージ状態: 未インストール"
                };
                EditorGUILayout.LabelField(statusText, EditorStyles.miniBoldLabel);
                if (GUILayout.Button("更新", GUILayout.Width(50)))
                {
                    RefreshInstalledState();
                }
            }

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private void DrawStepInstallPackage()
        {
            EditorGUILayout.LabelField("① UPMパッケージを追加", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Git URL経由でUnity CLI LoopパッケージをProjectに追加します。",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUI.DisabledScope(_isPackageInstalled == true))
            {
                if (GUILayout.Button("UPMパッケージを追加", GUILayout.Height(28)))
                {
                    InstallPackage();
                }
            }
        }

        private void DrawStepCheckNode()
        {
            EditorGUILayout.LabelField("② Node.js を確認", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("CLI/Skillsの導入にはNode.js 22.0以上が必要です。",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("node --version を実行", GUILayout.Height(24)))
            {
                RunCommand("Node.jsバージョン確認", "node --version");
            }
        }

        private void DrawStepInstallCli()
        {
            EditorGUILayout.LabelField("③ CLI (uloop-cli) をインストール", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("npm install -g uloop-cli を実行し、CLIをグローバルインストールします。",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("npm install -g uloop-cli を実行", GUILayout.Height(28)))
            {
                RunCommand("uloop-cli インストール", "npm install -g uloop-cli");
            }
        }

        private void DrawStepInstallSkills()
        {
            EditorGUILayout.LabelField("④ Skills を導入", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("導入先のAIツールとスコープを選択してSkillsを導入します。",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _skillsTarget = (SkillsTarget)EditorGUILayout.EnumPopup("導入先", _skillsTarget);
                _installGlobalSkills = EditorGUILayout.ToggleLeft("--global", _installGlobalSkills, GUILayout.Width(90));
            }

            if (GUILayout.Button("Skillsを導入", GUILayout.Height(28)))
            {
                string flag = _skillsTarget == SkillsTarget.Claude ? "--claude" : "--codex";
                string command = _installGlobalSkills
                    ? $"uloop skills install {flag} --global"
                    : $"uloop skills install {flag}";
                RunCommand("Skills導入", command);
            }
        }

        private void DrawStepOpenSettings()
        {
            EditorGUILayout.LabelField("⑤ MCP接続設定を開く", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "MCPクライアント（Claude Code等）へのmcp.json自動設定は、Unity CLI Loop本体の設定ウィンドウから行います。" +
                "パッケージ追加直後はドメインリロードが必要なため、少し待ってから開いてください。",
                MessageType.Info);

            if (GUILayout.Button("Unity CLI Loop の設定ウィンドウを開く", GUILayout.Height(28)))
            {
                if (!EditorApplication.ExecuteMenuItem(SettingsMenuPath))
                {
                    EditorUtility.DisplayDialog("Useful Toolkit",
                        "設定ウィンドウを開けませんでした。パッケージのインポート・コンパイルが完了してから再度お試しください。", "OK");
                }
            }
        }

        private void DrawLog(bool isBusy)
        {
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_lastCommandLabel) ? "実行ログ" : $"実行ログ: {_lastCommandLabel}",
                EditorStyles.boldLabel);

            string logText;
            lock (ProcessOutput)
            {
                logText = ProcessOutput.ToString();
            }

            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, GUI.skin.box, GUILayout.Height(140));
            EditorGUILayout.SelectableLabel(logText, EditorStyles.wordWrappedMiniLabel,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (isBusy)
            {
                EditorGUILayout.LabelField("実行中...", EditorStyles.miniLabel);
            }
        }

        private static bool IsBusy()
        {
            return _addRequest != null || _listRequest != null ||
                   (_runningProcess != null && !_runningProcess.HasExited);
        }

        //  UPM パッケージ管理

        private static void RefreshInstalledState()
        {
            _isPackageInstalled = null;
            _listRequest = Client.List(true, false);
            EditorApplication.update += CheckListRequest;
        }

        private static void CheckListRequest()
        {
            if (_listRequest == null || !_listRequest.IsCompleted) return;
            EditorApplication.update -= CheckListRequest;

            if (_listRequest.Status == StatusCode.Success)
            {
                bool found = false;
                foreach (var package in _listRequest.Result)
                {
                    if (package.name == PackageId)
                    {
                        found = true;
                        break;
                    }
                }

                _isPackageInstalled = found;
            }
            else
            {
                Debug.LogError($"[UsefulToolkit] パッケージ一覧の取得に失敗しました: {_listRequest.Error?.message}");
                _isPackageInstalled = null;
            }

            _listRequest = null;
        }

        private static void InstallPackage()
        {
            _addRequest = Client.Add(PackageGitUrl);
            EditorApplication.update += CheckAddRequest;
        }

        private static void CheckAddRequest()
        {
            if (_addRequest == null || !_addRequest.IsCompleted) return;
            EditorApplication.update -= CheckAddRequest;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[UsefulToolkit] Unity CLI Loop のインストールに成功しました: {_addRequest.Result.name}");
                _isPackageInstalled = true;
            }
            else if (_addRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[UsefulToolkit] Unity CLI Loop のインストールに失敗しました: {_addRequest.Error.message}");
                EditorUtility.DisplayDialog("Useful Toolkit", $"パッケージの追加に失敗しました:\n{_addRequest.Error.message}", "閉じる");
            }

            _addRequest = null;
        }

        //  外部コマンド実行（npm / uloop CLI）

        private static void RunCommand(string label, string command)
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                EditorUtility.DisplayDialog("Useful Toolkit", "既に他のコマンドを実行中です。完了までお待ちください。", "OK");
                return;
            }

            _lastCommandLabel = label;
            lock (ProcessOutput)
            {
                ProcessOutput.Clear();
                ProcessOutput.AppendLine($"$ {command}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            try
            {
                _runningProcess = new Process { StartInfo = startInfo };
                _runningProcess.OutputDataReceived += (_, e) => AppendLog(e.Data);
                _runningProcess.ErrorDataReceived += (_, e) => AppendLog(e.Data == null ? null : $"[stderr] {e.Data}");
                _runningProcess.Start();
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();
                EditorApplication.update += CheckRunningProcess;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UsefulToolkit] コマンド実行に失敗しました: {e.Message}");
                EditorUtility.DisplayDialog("Useful Toolkit",
                    $"コマンドの実行に失敗しました:\n{e.Message}\n\nNode.js / npm がインストールされ、PATHが通っているか確認してください。", "閉じる");
                _runningProcess = null;
            }
        }

        private static void AppendLog(string line)
        {
            if (line == null) return;
            lock (ProcessOutput)
            {
                ProcessOutput.AppendLine(line);
            }
        }

        private static void CheckRunningProcess()
        {
            if (_runningProcess == null || !_runningProcess.HasExited) return;
            EditorApplication.update -= CheckRunningProcess;

            int exitCode = _runningProcess.ExitCode;
            lock (ProcessOutput)
            {
                ProcessOutput.AppendLine($"--- 終了コード: {exitCode} ---");
            }

            if (exitCode == 0)
            {
                Debug.Log($"[UsefulToolkit] {_lastCommandLabel} が完了しました。");
            }
            else
            {
                Debug.LogError($"[UsefulToolkit] {_lastCommandLabel} が失敗しました（終了コード: {exitCode}）。ログを確認してください。");
            }

            _runningProcess.Dispose();
            _runningProcess = null;
        }
    }
}
