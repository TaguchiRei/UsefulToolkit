using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// UsefulToolkitのマルチパッケージ対応カスタムインストーラー
    /// </summary>
    public class UsefulToolkitInstaller : EditorWindow
    {
        //  基本設定 
        private const string RepositoryUrl = "https://github.com/TaguchiRei/UsefulToolkit.git";
        private const string ToolkitName = "Useful Toolkit";

        // パッケージの定義構造体
        private struct PackageInfo
        {
            public string DisplayName; // 画面表示名
            public string SubPath; // リポジトリ内のフォルダ名 (Packages/フォルダ名)
            public string Description; // 簡単な説明
            public bool IsRequired; // 必須かどうか
            public bool IsSelected; // チェック状態

            public PackageInfo(string name, string subPath, string desc, bool required = false)
            {
                DisplayName = name;
                SubPath = subPath;
                Description = desc;
                IsRequired = required;
                IsSelected = required; // 必須枠はデフォルトON
            }

            // UPMに渡すための完全なGit URLを生成
            public string GetFullIdentifier()
            {
                return $"{RepositoryUrl}?path=Packages/{SubPath}";
            }
        }

        //  パッケージリストの一覧定義 
        private static List<PackageInfo> _packages = new()
        {
            new PackageInfo("Framework", "com.ray.usefultoolkit.framework", "Toolkitのコア機能・共通基盤（インストール済み）", true),
            new PackageInfo("Attributes", "com.ray.usefultoolkit.attributes", "インスペクターや開発を強力に補助するカスタム属性群"),
            new PackageInfo("Debugging Tools", "com.ray.usefultoolkit.debuggingtools", "ログ拡張やランタイムデバッグを快適にするツール"),
            new PackageInfo("Program Tools", "com.ray.usefultoolkit.programtools", "汎用的な最適化・コードヘルパー・ロジック集"),
            new PackageInfo("Ai Agent Tools", "com.ray.usefultoolkit.aiagenttools", "AIやステートマシン、エージェント作成の支援機能"),
            new PackageInfo("Networking", "com.ray.usefultoolkit.networking", "通信処理やオンライン周りのラッパー・拡張"),
            new PackageInfo("Quality Control Tools", "com.ray.usefultoolkit.qualitycontroltools",
                "テストや静的解析、品質管理をサポートする機能"),
            new PackageInfo("Sound Art Tools", "com.ray.usefultoolkit.soundarttools", "サウンド管理、再生制御、オーディオ演出システム"),
            new PackageInfo("Visual Art Tools", "com.ray.usefultoolkit.visualarttools",
                "グラフィックス、エフェクト、見た目に関する演出コンポーネント"),
            new PackageInfo("Static Data Tools", "com.ray.usefultoolkit.staticdatatools",
                "ScriptableObjectやマスターデータの管理・運用ツール"),
        };

        // キュー管理と状態
        private static Queue<string> _installQueue = new();
        private static AddRequest _currentRequest;
        private static int _totalInstallCount;
        private static int _currentInstallIndex;
        private Vector2 _scrollPosition;

        [MenuItem("UsefulToolkit/Installer")]
        public static void ShowWindow()
        {
            var window = GetWindow<UsefulToolkitInstaller>("Toolkit Installer");
            window.minSize = new Vector2(500, 450);
            window.maxSize = new Vector2(500, 700);
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
            bool isProcessing = IsProcessing();
            EditorGUI.BeginDisabledGroup(isProcessing);
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("全て選択", EditorStyles.miniButtonLeft))
                {
                    SetAllSelection(true);
                }

                if (GUILayout.Button("全て解除", EditorStyles.miniButtonRight))
                {
                    SetAllSelection(false);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawPackageList()
        {
            bool isProcessing = IsProcessing();

            for (int i = 0; i < _packages.Count; i++)
            {
                var pkg = _packages[i];

                EditorGUILayout.BeginHorizontal();
                {
                    // 処理中、または必須枠（Framework自身）はトグルを変更させない
                    EditorGUI.BeginDisabledGroup(isProcessing || pkg.IsRequired);
                    {
                        pkg.IsSelected =
                            EditorGUILayout.Toggle(pkg.IsSelected, GUILayout.Width(20), GUILayout.Height(32));
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.BeginVertical();
                    {
                        var nameStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                        EditorGUILayout.LabelField(pkg.DisplayName + (pkg.IsRequired ? " (Core / Installed)" : ""),
                            nameStyle);

                        var descStyle = new GUIStyle(EditorStyles.miniLabel)
                            { wordWrap = true, normal = { textColor = Color.gray } };
                        EditorGUILayout.LabelField(pkg.Description, descStyle);
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();

                _packages[i] = pkg;

                // 境界線
                if (i < _packages.Count - 1)
                {
                    Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
                    EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
                    GUILayout.Space(2);
                }
            }
        }

        private void DrawInstallSection()
        {
            bool isProcessing = IsProcessing();

            EditorGUI.BeginDisabledGroup(isProcessing);
            {
                var buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 14,
                    fixedHeight = 45,
                    fontStyle = FontStyle.Bold
                };

                string buttonText = isProcessing
                    ? $"インストール中 ({_currentInstallIndex}/{_totalInstallCount})..."
                    : "選択した機能をインポート";

                if (GUILayout.Button(buttonText, buttonStyle))
                {
                    _installQueue.Clear();
                    foreach (var pkg in _packages)
                    {
                        if (pkg.IsSelected && !pkg.IsRequired)
                        {
                            _installQueue.Enqueue(pkg.GetFullIdentifier());
                        }
                    }

                    if (_installQueue.Count == 0)
                    {
                        EditorUtility.DisplayDialog("Useful Toolkit", "インポートする追加パッケージが選択されていません。", "OK");
                        return;
                    }

                    if (EditorUtility.DisplayDialog("Useful Toolkit", $"{_installQueue.Count} 個のモジュールを順番にインストールしますか？",
                            "はい", "いいえ"))
                    {
                        _totalInstallCount = _installQueue.Count;
                        _currentInstallIndex = 0;
                        ProcessNextQueue();
                    }
                }
            }
            EditorGUI.EndDisabledGroup();

            if (isProcessing)
            {
                GUILayout.Space(10);
                float progress = (float)_currentInstallIndex / _totalInstallCount;
                Rect progressRect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(progressRect, progress,
                    $"処理中... ({_currentInstallIndex} / {_totalInstallCount})");
            }
        }

        private static bool IsProcessing()
        {
            return _installQueue.Count > 0 || (_currentRequest != null && !_currentRequest.IsCompleted);
        }

        private void SetAllSelection(bool select)
        {
            for (int i = 0; i < _packages.Count; i++)
            {
                if (!_packages[i].IsRequired)
                {
                    var pkg = _packages[i];
                    pkg.IsSelected = select;
                    _packages[i] = pkg;
                }
            }
        }

        //  UPM 連続処理エンジン 

        private static void ProcessNextQueue()
        {
            if (_installQueue.Count == 0)
            {
                _currentRequest = null;
                EditorApplication.update -= ProgressCallback;
                EditorUtility.DisplayDialog("Useful Toolkit", "選択されたすべてのモジュールのインポートが完了しました！", "OK");
                return;
            }

            string nextUrl = _installQueue.Dequeue();
            _currentInstallIndex++;

            _currentRequest = Client.Add(nextUrl);

            if (_currentInstallIndex == 1)
            {
                EditorApplication.update += ProgressCallback;
            }
        }

        private static void ProgressCallback()
        {
            if (_currentRequest == null) return;

            if (_currentRequest.IsCompleted)
            {
                if (_currentRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"[UsefulToolkit] インストール成功: {_currentRequest.Result.name}");
                }
                else if (_currentRequest.Status >= StatusCode.Failure)
                {
                    Debug.LogError($"[UsefulToolkit] インストール失敗: {_currentRequest.Error.message}");
                    EditorUtility.DisplayDialog("エラー",
                        $"インストールの途中でエラーが発生しました。処理を中断します:\n{_currentRequest.Error.message}", "閉じる");

                    _installQueue.Clear();
                    _currentRequest = null;
                    EditorApplication.update -= ProgressCallback;
                    return;
                }

                ProcessNextQueue();
            }
        }
    }
}
