using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UsefulToolkit.Editor.ProjectSettings
{
    internal sealed class PackageSection : IProjectSettingsSection
    {
        public string Title => "Package / Tools";

        // GitHub リポジトリのURL
        private const string RepositoryUrl = "https://github.com/TaguchiRei/UsefulToolkit.git";

        private bool _openPackageSection;

        // 管理対象のパッケージ一覧
        private static readonly List<PackageInfoItem> TargetPackages = new()
        {
            new("com.rei.usefultoolkit.aitool", "AI Tool", "Packages/com.rei.usefultoolkit.aitool"),
            new("com.rei.usefultoolkit.architecture", "Architecture", "Packages/com.rei.usefultoolkit.architecture"),
            new("com.rei.usefultoolkit.debugging", "Debugging", "Packages/com.rei.usefultoolkit.debugging"),
            new("com.rei.usefultoolkit.gitsupport", "Git Support", "Packages/com.rei.usefultoolkit.gitsupport"),
            new("com.rei.usefultoolkit.networking", "Networking", "Packages/com.rei.usefultoolkit.networking"),
            new("com.rei.usefultoolkit.programtools", "Program Tools", "Packages/com.rei.usefultoolkit.programtools"),
            new("com.rei.usefultoolkit.qualitycontroltools", "Quality Control Tools",
                "Packages/com.rei.usefultoolkit.qualitycontroltools"),
            new("com.rei.usefultoolkit.soundarttools", "Sound Art Tools",
                "Packages/com.rei.usefultoolkit.soundarttools"),
            new("com.rei.usefultoolkit.staticdatatools", "Static Data Tools",
                "Packages/com.rei.usefultoolkit.staticdatatools"),
            new("com.rei.usefultoolkit.visualarttools", "Visual Art Tools",
                "Packages/com.rei.usefultoolkit.visualarttools"),
        };

        private static HashSet<string> _installedPackageIds = new();
        private static Request _currentRequest;
        private static bool _isInitialized;

        public PackageSection()
        {
            if (!_isInitialized)
            {
                RefreshInstalledPackages();
                _isInitialized = true;
            }
        }

        public void OnGUI()
        {
            bool isProcessing = _currentRequest != null && !_currentRequest.IsCompleted;

            using (new EditorGUI.DisabledScope(isProcessing))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Available Sub-Packages", EditorStyles.boldLabel);

                if (GUILayout.Button("Status Refresh", GUILayout.Width(120)))
                {
                    RefreshInstalledPackages();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox("GitHubからサブモジュール/サブフォルダ単位で各ツールを個別インストール・削除できます。", MessageType.Info);
                EditorGUILayout.Space(5);

                _openPackageSection = EditorGUILayout.Foldout(_openPackageSection, "Package Section", true);

                if (_openPackageSection)
                {
                    foreach (var item in TargetPackages)
                    {
                        bool isInstalled = _installedPackageIds.Contains(item.PackageId);

                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.BeginVertical();
                            EditorGUILayout.LabelField(item.DisplayName, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField(item.PackageId, EditorStyles.miniLabel);
                            EditorGUILayout.EndVertical();

                            GUILayout.FlexibleSpace();

                            if (isInstalled)
                            {
                                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                                if (GUILayout.Button("Uninstall", GUILayout.Width(100), GUILayout.Height(24)))
                                {
                                    RemovePackage(item.PackageId);
                                }

                                GUI.backgroundColor = Color.white;
                            }
                            else
                            {
                                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                                if (GUILayout.Button("Install", GUILayout.Width(100), GUILayout.Height(24)))
                                {
                                    InstallPackage(item.SubFolderPath);
                                }

                                GUI.backgroundColor = Color.white;
                            }
                        }
                    }
                }
            }

            MonitorRequest();
        }

        private static void InstallPackage(string subFolderPath)
        {
            // Unity Package Manager の Git Subdirectory 参照構文 (?path=)
            string packageGitUrl = $"{RepositoryUrl}?path={subFolderPath}";
            _currentRequest = Client.Add(packageGitUrl);
            EditorApplication.update += ProgressUpdate;
        }

        private static void RemovePackage(string packageId)
        {
            _currentRequest = Client.Remove(packageId);
            EditorApplication.update += ProgressUpdate;
        }

        private static void RefreshInstalledPackages()
        {
            var listReq = Client.List(true);

            void OnUpdate()
            {
                if (!listReq.IsCompleted) return;

                if (listReq.Status == StatusCode.Success)
                {
                    _installedPackageIds = new HashSet<string>(listReq.Result.Select(p => p.name));
                }
                else
                {
                    Debug.LogError($"[UsefulToolkit] 導入済みパッケージ一覧の取得に失敗しました: {listReq.Error.message}");
                }

                EditorApplication.update -= OnUpdate;
            }

            EditorApplication.update += OnUpdate;
        }

        private static void ProgressUpdate()
        {
            if (_currentRequest == null || !_currentRequest.IsCompleted) return;

            if (_currentRequest.Status == StatusCode.Success)
            {
                Debug.Log("[UsefulToolkit] パッケージ操作が正常に完了しました。");
                RefreshInstalledPackages();
            }
            else if (_currentRequest.Status >= StatusCode.Failure)
            {
                Debug.LogError($"[UsefulToolkit] パッケージ操作に失敗しました: {_currentRequest.Error.message}");
            }

            EditorApplication.update -= ProgressUpdate;
            _currentRequest = null;
        }

        private static void MonitorRequest()
        {
            if (_currentRequest != null && !_currentRequest.IsCompleted)
            {
                EditorGUILayout.HelpBox("Package Manager で処理を実行中...", MessageType.Warning);
            }
        }

        private readonly struct PackageInfoItem
        {
            public string PackageId { get; }
            public string DisplayName { get; }
            public string SubFolderPath { get; }

            public PackageInfoItem(string packageId, string displayName, string subFolderPath)
            {
                PackageId = packageId;
                DisplayName = displayName;
                SubFolderPath = subFolderPath;
            }
        }
    }
}