using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UsefulToolkit.Editor.Setting
{
    /// <summary>
    /// UsefulToolkitのマルチパッケージ対応カスタムインストーラー
    /// </summary>
    public class UsefulToolkitInstaller : EditorWindow
    {
        //  基本設定
        private const string RepositoryUrl = "https://github.com/TaguchiRei/UsefulToolkit.git";
        private const string ToolkitName = "Useful Toolkit";

        // UniTask (Cysharp) : Frameworkのランタイムが直接依存している外部必須パッケージ
        private const string UniTaskPackageName = "com.cysharp.unitask";

        private const string UniTaskUrl =
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";

        // パッケージ追加後は必ずドメインリロードが走りstaticが飛ぶため、跨ぎたい情報はSessionStateへ退避する
        private const string SelectionStateKey = "UsefulToolkit.Installer.Selection";
        private const string SelectionSavedStateKey = "UsefulToolkit.Installer.SelectionSaved";
        private const string PendingStateKey = "UsefulToolkit.Installer.Pending";
        private const string PendingOpStateKey = "UsefulToolkit.Installer.PendingOp";
        private const string StateSeparator = ";";
        private static readonly char[] StateSeparators = { ';' };

        // パッケージ操作の種別。パッケージ追加/削除の度にドメインリロードが走って状態が飛ぶため、
        // リロードを跨いで結果を検証・報告するときに文言を切り替える用途で使う。
        private enum PackageOperation
        {
            Install,
            Uninstall,
            Update,
        }

        // 外部（Toolkit外）の必須依存パッケージ
        private readonly struct ExternalDependency
        {
            public string PackageName { get; } // インストール済み判定に使うUPM上のパッケージ名
            public string Identifier { get; } // UPMに渡す完全な識別子（Git URL等）

            public ExternalDependency(string packageName, string identifier)
            {
                PackageName = packageName;
                Identifier = identifier;
            }
        }

        // パッケージの定義構造体
        private struct PackageInfo
        {
            public string DisplayName; // 画面表示名
            public string PackageName; // リポジトリ内のフォルダ名 (Packages/フォルダ名) 兼 UPM上のパッケージ名
            public string Description; // 簡単な説明
            public bool IsRequired; // 必須かどうか
            public bool IsSelected; // チェック状態
            public ExternalDependency[] RequiredDependencies; // このパッケージが動作するために別途インストールが必要な外部パッケージ

            public PackageInfo(string name, string packageName, string desc, bool required = false,
                ExternalDependency[] requiredDependencies = null)
            {
                DisplayName = name;
                PackageName = packageName;
                Description = desc;
                IsRequired = required;
                IsSelected = required; // 必須枠はデフォルトON
                RequiredDependencies = requiredDependencies ?? Array.Empty<ExternalDependency>();
            }

            // UPMに渡すための完全なGit URLを生成
            public string GetFullIdentifier()
            {
                return $"{RepositoryUrl}?path=Packages/{PackageName}";
            }
        }

        //  パッケージリストの一覧定義
        private static List<PackageInfo> _packages = new()
        {
            new PackageInfo("Framework", "com.rei.usefultoolkit.framework", "Toolkitのコア機能・共通基盤とコンポジションルート/初期化順序制御", true,
                new[] { new ExternalDependency(UniTaskPackageName, UniTaskUrl) }),
            new PackageInfo("Debugging Tools", "com.rei.usefultoolkit.debugging", "ログ拡張やランタイムデバッグを快適にするツール"),
            new PackageInfo("Git Support", "com.rei.usefultoolkit.gitsupport", "Gitignoreサポートやブランチ管理などVCS周りの補助機能"),
            new PackageInfo("Program Tools", "com.rei.usefultoolkit.programtools", "汎用的な最適化・コードヘルパー・ロジック集"),
            new PackageInfo("Ai Agent Tools", "com.rei.usefultoolkit.aitool", "AIやステートマシン、エージェント作成の支援機能"),
            new PackageInfo("Input", "com.rei.usefultoolkit.input",
                "InputSystemをState-Centrism ArchitectureへつなぐBlackBoard/EngineServiceLayer実装"),
            new PackageInfo("Networking", "com.rei.usefultoolkit.networking", "通信処理やオンライン周りのラッパー・拡張"),
            new PackageInfo("Quality Control Tools", "com.rei.usefultoolkit.qualitycontroltools",
                "テストや静的解析、品質管理をサポートする機能"),
            new PackageInfo("Sound Art Tools", "com.rei.usefultoolkit.soundarttools", "サウンド管理、再生制御、オーディオ演出システム"),
            new PackageInfo("Visual Art Tools", "com.rei.usefultoolkit.visualarttools",
                "グラフィックス、エフェクト、見た目に関する演出コンポーネント"),
            new PackageInfo("Mesh Cut", "com.rei.usefultoolkit.meshcut",
                "Burst + Job Systemで複数メッシュを一枚の刃で一括切断するライブラリ", false,
                new[] { new ExternalDependency(UniTaskPackageName, UniTaskUrl) }),
            new PackageInfo("Static Data Tools", "com.rei.usefultoolkit.staticdatatools",
                "ScriptableObjectやマスターデータの管理・運用ツール"),
            new PackageInfo("WorkTrack", "com.rei.usefultoolkit.worktrack", "Unity Editorでの作業時間を自動記録・閲覧するツール"),
        };

        // 導入状況とリクエストの状態
        private static readonly HashSet<string> InstalledPackageNames = new();
        private static ListRequest _listRequest;
        private static AddAndRemoveRequest _addRequest;
        private static PackageOperation _pendingOperation = PackageOperation.Install;
        private static Action _onListCompleted;
        private static string _statusMessage;
        private static MessageType _statusType = MessageType.Info;
        private Vector2 _scrollPosition;

        [MenuItem("UsefulToolkit/Installer")]
        public static void ShowWindow()
        {
            var window = GetWindow<UsefulToolkitInstaller>("Toolkit Installer");
            window.minSize = new Vector2(500, 450);
            window.maxSize = new Vector2(500, 700);
        }

        private void OnEnable()
        {
            // ドメインリロード後もウィンドウ自体は復元されるが、staticのチェック状態は初期化されているので復元する
            RestoreSelection();
            RefreshInstalledPackages();
        }

        private void OnGUI()
        {
            // 全体に余白を設定
            Rect area = new Rect(15, 15, position.width - 30, position.height - 30);
            GUILayout.BeginArea(area);

            // 1. ヘッダー
            DrawHeader();
            GUILayout.Space(12);

            // 一括選択・解除ショートカットボタン
            DrawShortcutButtons();
            GUILayout.Space(8);

            // 2. スクロール可能なパッケージリスト
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUI.skin.box);
            DrawPackageList();
            EditorGUILayout.EndScrollView();

            GUILayout.Space(15);

            // 3. インストール実行エリア
            DrawInstallSection();

            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }
            };
            EditorGUILayout.LabelField($"{ToolkitName} Dashboard", titleStyle, GUILayout.Height(25));

            var repoStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.gray } };
            EditorGUILayout.LabelField($"Src: {RepositoryUrl}", repoStyle);

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private void DrawShortcutButtons()
        {
            EditorGUI.BeginDisabledGroup(IsBusy());
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("全て選択", EditorStyles.miniButtonLeft))
                {
                    SetAllSelection(true);
                }

                if (GUILayout.Button("全て解除", EditorStyles.miniButtonMid))
                {
                    SetAllSelection(false);
                }

                if (GUILayout.Button("導入状況を再取得", EditorStyles.miniButtonRight))
                {
                    RefreshInstalledPackages();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawPackageList()
        {
            bool isBusy = IsBusy();

            for (int i = 0; i < _packages.Count; i++)
            {
                var pkg = _packages[i];
                bool isInstalled = IsInstalled(pkg.PackageName);

                // 処理中、必須枠（Framework自身）、導入済みのものはトグルを変更させない
                bool isLocked = isBusy || pkg.IsRequired || isInstalled;

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginDisabledGroup(isLocked);
                    {
                        bool toggled = EditorGUILayout.Toggle(isInstalled || pkg.IsSelected, GUILayout.Width(20),
                            GUILayout.Height(32));
                        if (!isLocked && toggled != pkg.IsSelected)
                        {
                            pkg.IsSelected = toggled;
                            _packages[i] = pkg;
                            SaveSelection();
                        }
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.BeginVertical();
                    {
                        var nameStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                        if (isInstalled)
                        {
                            nameStyle.normal.textColor = EditorGUIUtility.isProSkin
                                ? new Color(0.45f, 0.85f, 0.5f)
                                : new Color(0.1f, 0.5f, 0.15f);
                        }

                        EditorGUILayout.LabelField(pkg.DisplayName + BuildStatusSuffix(pkg, isInstalled), nameStyle);

                        var descStyle = new GUIStyle(EditorStyles.miniLabel)
                            { wordWrap = true, normal = { textColor = Color.gray } };
                        EditorGUILayout.LabelField(pkg.Description, descStyle);
                    }
                    EditorGUILayout.EndVertical();

                    // 導入済みかつ必須枠でないものは、その場でアンインストールできる
                    if (isInstalled && !pkg.IsRequired)
                    {
                        EditorGUI.BeginDisabledGroup(isBusy);
                        {
                            Color prevBg = GUI.backgroundColor;
                            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                            if (GUILayout.Button("削除", GUILayout.Width(56), GUILayout.Height(32)))
                            {
                                StartUninstall(pkg);
                            }

                            GUI.backgroundColor = prevBg;
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 境界線
                if (i < _packages.Count - 1)
                {
                    Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
                    EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
                    GUILayout.Space(2);
                }
            }
        }

        private static string BuildStatusSuffix(PackageInfo pkg, bool isInstalled)
        {
            if (isInstalled)
            {
                return pkg.IsRequired ? "  (Core / インストール済み)" : "  (インストール済み)";
            }

            return pkg.IsRequired ? "  (Core)" : "  (未インストール)";
        }

        private void DrawInstallSection()
        {
            bool isInstalling = IsInstalling();
            bool isLoadingStatus = IsLoadingStatus();

            EditorGUI.BeginDisabledGroup(isInstalling || isLoadingStatus);
            {
                var buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 14,
                    fixedHeight = 45,
                    fontStyle = FontStyle.Bold
                };

                string buttonText = isInstalling ? "インストール中..." : "選択した機能をインポート";

                if (GUILayout.Button(buttonText, buttonStyle))
                {
                    StartInstall();
                }
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);

            // 導入済みのUsefulToolkitパッケージをまとめて最新コミットへ更新する（外部依存は対象外）
            bool anyToolkitInstalled = _packages.Any(p => IsInstalled(p.PackageName));
            EditorGUI.BeginDisabledGroup(isInstalling || isLoadingStatus || !anyToolkitInstalled);
            {
                string updateText = isInstalling ? "処理中..." : "導入済みを一括アップデート";
                if (GUILayout.Button(updateText, GUILayout.Height(26)))
                {
                    StartUpdateAll();
                }
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);

            if (isLoadingStatus)
            {
                EditorGUILayout.HelpBox("導入状況を取得中です...", MessageType.Info);
            }
            else if (isInstalling)
            {
                EditorGUILayout.HelpBox("Package Manager がパッケージを解決中です。完了後にスクリプトの再コンパイルが走ります。",
                    MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        private static bool IsInstalling()
        {
            return _addRequest != null && !_addRequest.IsCompleted;
        }

        private static bool IsLoadingStatus()
        {
            return _listRequest != null && !_listRequest.IsCompleted;
        }

        private static bool IsBusy()
        {
            return IsInstalling() || IsLoadingStatus();
        }

        private static bool IsInstalled(string packageName)
        {
            return InstalledPackageNames.Contains(packageName);
        }

        private void SetAllSelection(bool select)
        {
            for (int i = 0; i < _packages.Count; i++)
            {
                if (_packages[i].IsRequired) continue;

                var pkg = _packages[i];
                pkg.IsSelected = select;
                _packages[i] = pkg;
            }

            SaveSelection();
        }

        //  UPM 処理

        private void StartInstall()
        {
            var identifiers = new List<string>();
            var packageNames = new List<string>();
            var queued = new HashSet<string>();

            foreach (var pkg in _packages)
            {
                bool isInstalled = IsInstalled(pkg.PackageName);
                bool isWanted = pkg.IsSelected || pkg.IsRequired;
                if (!isWanted && !isInstalled) continue;

                // 必須枠（Framework等）や導入済みのものは本体こそ積まないが、その依存パッケージは常に確保する
                foreach (var dependency in pkg.RequiredDependencies)
                {
                    if (IsInstalled(dependency.PackageName)) continue;
                    if (!queued.Add(dependency.Identifier)) continue;

                    identifiers.Add(dependency.Identifier);
                    packageNames.Add(dependency.PackageName);
                }

                if (isInstalled || pkg.IsRequired) continue;
                if (!queued.Add(pkg.GetFullIdentifier())) continue;

                identifiers.Add(pkg.GetFullIdentifier());
                packageNames.Add(pkg.PackageName);
            }

            if (identifiers.Count == 0)
            {
                EditorUtility.DisplayDialog(ToolkitName, "追加が必要なパッケージはありません。（選択中のものはすべて導入済みです）", "OK");
                return;
            }

            string detail = string.Join("\n", packageNames.Select(name => "・" + name));
            if (!EditorUtility.DisplayDialog(ToolkitName,
                    $"{identifiers.Count} 個のモジュールをまとめてインストールしますか？（必須の依存パッケージを含みます）\n\n{detail}",
                    "はい", "いいえ"))
            {
                return;
            }

            SaveSelection();
            ExecuteRequest(PackageOperation.Install, identifiers.ToArray(), Array.Empty<string>(), packageNames);
        }

        /// <summary>
        /// 指定パッケージ単体をアンインストールする。必須枠（Framework）は対象外。
        /// </summary>
        private void StartUninstall(PackageInfo pkg)
        {
            if (pkg.IsRequired) return;

            if (!EditorUtility.DisplayDialog(ToolkitName,
                    $"{pkg.DisplayName}（{pkg.PackageName}）をアンインストールしますか？\n\n" +
                    "このパッケージに依存している他パッケージが残っている場合、UPM 側で削除が拒否されることがあります。",
                    "アンインストール", "キャンセル"))
            {
                return;
            }

            SaveSelection();
            ExecuteRequest(PackageOperation.Uninstall, Array.Empty<string>(), new[] { pkg.PackageName },
                new[] { pkg.PackageName });
        }

        /// <summary>
        /// 導入済みのUsefulToolkitパッケージをすべて Git URL で再Addし、リモート既定ブランチの最新コミットへ再解決する。
        /// 外部依存パッケージ（UniTask等）は対象にしない。
        /// </summary>
        private void StartUpdateAll()
        {
            var identifiers = new List<string>();
            var packageNames = new List<string>();

            foreach (var pkg in _packages)
            {
                if (!IsInstalled(pkg.PackageName)) continue;

                identifiers.Add(pkg.GetFullIdentifier());
                packageNames.Add(pkg.PackageName);
            }

            if (identifiers.Count == 0)
            {
                EditorUtility.DisplayDialog(ToolkitName, "アップデート対象の導入済みパッケージがありません。", "OK");
                return;
            }

            string detail = string.Join("\n", packageNames.Select(name => "・" + name));
            if (!EditorUtility.DisplayDialog(ToolkitName,
                    $"導入済みの {identifiers.Count} 個のパッケージをリモートの最新コミットへ更新しますか？\n" +
                    "（外部依存パッケージは対象外です）\n\n" + detail,
                    "アップデート", "キャンセル"))
            {
                return;
            }

            SaveSelection();
            ExecuteRequest(PackageOperation.Update, identifiers.ToArray(), Array.Empty<string>(), packageNames);
        }

        /// <summary>
        /// Add/Remove を1リクエストにまとめて投げる。
        /// 解決後のドメインリロードでリクエストごと状態が飛ぶため、結果検証用の名前と操作種別をSessionStateへ退避しておく。
        /// </summary>
        private static void ExecuteRequest(PackageOperation operation, string[] toAdd, string[] toRemove,
            IEnumerable<string> resultCheckNames)
        {
            _pendingOperation = operation;
            SessionState.SetString(PendingStateKey, string.Join(StateSeparator, resultCheckNames));
            SessionState.SetString(PendingOpStateKey, operation.ToString());
            _statusMessage = null;

            // Client.Addを1件ずつ回すと操作の度にドメインリロードが走ってキューが消えるため、1リクエストにまとめて投げる
            _addRequest = Client.AddAndRemove(toAdd, toRemove);
            EditorApplication.update += AddProgressCallback;
        }

        private static string OperationLabel(PackageOperation operation)
        {
            return operation switch
            {
                PackageOperation.Uninstall => "アンインストール",
                PackageOperation.Update => "アップデート",
                _ => "インストール",
            };
        }

        private static void AddProgressCallback()
        {
            if (_addRequest == null || !_addRequest.IsCompleted) return;

            EditorApplication.update -= AddProgressCallback;
            var request = _addRequest;
            _addRequest = null;
            SessionState.EraseString(PendingStateKey);
            SessionState.EraseString(PendingOpStateKey);

            string opLabel = OperationLabel(_pendingOperation);

            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[UsefulToolkit] 選択されたモジュールの{opLabel}が完了しました。");
                SetStatus($"選択されたモジュールの{opLabel}が完了しました。", MessageType.Info);
            }
            else if (request.Status >= StatusCode.Failure)
            {
                string message = request.Error != null ? request.Error.message : "不明なエラー";
                Debug.LogError($"[UsefulToolkit] {opLabel}に失敗しました: {message}");
                SetStatus($"{opLabel}に失敗しました:\n{message}", MessageType.Error);
            }

            _pendingOperation = PackageOperation.Install;
            RefreshInstalledPackages();
        }

        /// <summary>
        /// ドメインリロードでリクエストが失われた場合の後始末。
        /// リロードされた時点で解決自体は終わっているため、実際の導入状況と突き合わせて結果を報告する。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            string pending = SessionState.GetString(PendingStateKey, string.Empty);
            if (string.IsNullOrEmpty(pending)) return;

            SessionState.EraseString(PendingStateKey);

            string opName = SessionState.GetString(PendingOpStateKey, PackageOperation.Install.ToString());
            SessionState.EraseString(PendingOpStateKey);
            var operation = Enum.TryParse(opName, out PackageOperation parsed) ? parsed : PackageOperation.Install;
            string opLabel = OperationLabel(operation);

            var expected = pending.Split(StateSeparators, StringSplitOptions.RemoveEmptyEntries);

            RefreshInstalledPackages(() =>
            {
                if (operation == PackageOperation.Uninstall)
                {
                    var remaining = expected.Where(IsInstalled).ToArray();
                    if (remaining.Length == 0)
                    {
                        Debug.Log($"[UsefulToolkit] {opLabel}が完了しました（{expected.Length} 個）。");
                        SetStatus($"{opLabel}が完了しました（{expected.Length} 個）。", MessageType.Info);
                        return;
                    }

                    string stillHere = string.Join("\n", remaining.Select(name => "・" + name));
                    Debug.LogError($"[UsefulToolkit] 次のパッケージが削除されていません:\n{stillHere}");
                    SetStatus($"次のパッケージが削除されていません:\n{stillHere}", MessageType.Error);
                    return;
                }

                var missing = expected.Where(name => !IsInstalled(name)).ToArray();
                if (missing.Length == 0)
                {
                    Debug.Log($"[UsefulToolkit] {opLabel}が完了しました（{expected.Length} 個）。");
                    SetStatus($"{opLabel}が完了しました（{expected.Length} 個）。", MessageType.Info);
                    return;
                }

                string detail = string.Join("\n", missing.Select(name => "・" + name));
                Debug.LogError($"[UsefulToolkit] 次のパッケージが導入されていません:\n{detail}");
                SetStatus($"次のパッケージが導入されていません:\n{detail}", MessageType.Error);
            });
        }

        private static void RefreshInstalledPackages(Action onCompleted = null)
        {
            if (onCompleted != null)
            {
                _onListCompleted += onCompleted;
            }

            if (IsLoadingStatus()) return;

            _listRequest = Client.List(true, false);
            EditorApplication.update += ListProgressCallback;
            RepaintWindows();
        }

        private static void ListProgressCallback()
        {
            if (_listRequest == null || !_listRequest.IsCompleted) return;

            EditorApplication.update -= ListProgressCallback;
            var request = _listRequest;
            _listRequest = null;

            if (request.Status == StatusCode.Success)
            {
                InstalledPackageNames.Clear();
                foreach (var package in request.Result)
                {
                    InstalledPackageNames.Add(package.name);
                }
            }
            else if (request.Status >= StatusCode.Failure)
            {
                string message = request.Error != null ? request.Error.message : "不明なエラー";
                Debug.LogError($"[UsefulToolkit] 導入済みパッケージ一覧の取得に失敗しました: {message}");
            }

            var callback = _onListCompleted;
            _onListCompleted = null;
            callback?.Invoke();

            RepaintWindows();
        }

        private static void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            RepaintWindows();
        }

        private static void RepaintWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<UsefulToolkitInstaller>())
            {
                window.Repaint();
            }
        }

        //  チェック状態の永続化（ドメインリロード対策。エディタを閉じるまで有効）

        private static void SaveSelection()
        {
            var selected = _packages.Where(pkg => pkg.IsSelected && !pkg.IsRequired).Select(pkg => pkg.PackageName);
            SessionState.SetString(SelectionStateKey, string.Join(StateSeparator, selected));
            SessionState.SetBool(SelectionSavedStateKey, true);
        }

        private static void RestoreSelection()
        {
            if (!SessionState.GetBool(SelectionSavedStateKey, false)) return;

            var selected = new HashSet<string>(SessionState.GetString(SelectionStateKey, string.Empty)
                .Split(StateSeparators, StringSplitOptions.RemoveEmptyEntries));

            for (int i = 0; i < _packages.Count; i++)
            {
                var pkg = _packages[i];
                pkg.IsSelected = pkg.IsRequired || selected.Contains(pkg.PackageName);
                _packages[i] = pkg;
            }
        }
    }
}
