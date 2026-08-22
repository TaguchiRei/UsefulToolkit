using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UsefulToolkit.Editor.ProgramTools
{
    public class MultiFileSearchWindow : EditorWindow
    {
        [Serializable]
        private class SearchResult
        {
            public string Path;
            public int LineNum;
            public string Preview;
            public string RawText;
        }

        private enum ToolMode
        {
            SearchAndReplace,
            CombineFiles,
            FolderStructure
        }

        private enum FindType
        {
            Word,
            Subsequence
        }

        [SerializeField] private ToolMode _mode = ToolMode.SearchAndReplace;
        [SerializeField] private string _searchKeyword = "";
        [SerializeField] private string _replaceKeyword = "";
        [SerializeField] private string _extension = ".cs";
        [SerializeField] private string _rootFolder = "";
        [SerializeField] private bool _includeSubfolders = true; // 結合・構造出力用
        [SerializeField] private bool _ignoreLineBreaks = false;
        [SerializeField] private bool _ignoreSpace = false;
        [SerializeField] private FindType _findType = FindType.Word;

        [SerializeField] private List<string> _searchKeyWords = new() { "", "" };
        [SerializeField] private List<string> _replaceKeyWords = new() { "", "" };
        [SerializeField] private List<SearchResult> results = new();

        private Vector2 _scrollPosition;

        [MenuItem("UsefulToolkit/ProgramTools/MultiFileSearchWindow")]
        private static void Open()
        {
            GetWindow<MultiFileSearchWindow>("MultiFileSearchWindow");
        }

        private void OnGUI()
        {
            // モード切替タブ
            _mode = (ToolMode)GUILayout.Toolbar((int)_mode,
                new string[] { "Search / Replace", "Combine Files", "Folder Structure" });
            EditorGUILayout.Space(5);

            // 共通設定部 
            EditorGUILayout.LabelField("Common Settings", EditorStyles.boldLabel);
            _extension = EditorGUILayout.TextField("Extension", _extension);

            EditorGUILayout.BeginHorizontal();
            _rootFolder = EditorGUILayout.TextField("Root Folder", _rootFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string defaultPath = string.IsNullOrEmpty(_rootFolder) ? UnityEngine.Application.dataPath : _rootFolder;
                _rootFolder = EditorUtility.OpenFolderPanel("Select Root Folder", defaultPath, "");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // モードごとの画面描画
            switch (_mode)
            {
                case ToolMode.SearchAndReplace:
                    DrawSearchAndReplaceGUI();
                    break;
                case ToolMode.CombineFiles:
                    DrawCombineFilesGUI();
                    break;
                case ToolMode.FolderStructure:
                    DrawFolderStructureGUI();
                    break;
            }
        }

        #region Search & Replace GUI

        private void DrawSearchAndReplaceGUI()
        {
            if (_findType == FindType.Word)
            {
                EditorGUILayout.LabelField("Word Search Mode", EditorStyles.boldLabel);
                _searchKeyword = EditorGUILayout.TextField("Search Keyword", _searchKeyword);
                _replaceKeyword = EditorGUILayout.TextField("Replace Keyword", _replaceKeyword);
            }
            else
            {
                EditorGUILayout.LabelField("Subsequence Search Mode", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
                for (int i = 0; i < _searchKeyWords.Count; i++)
                {
                    _searchKeyWords[i] = EditorGUILayout.TextField(_searchKeyWords[i], GUILayout.MinWidth(60));
                }

                if (GUILayout.Button("+", GUILayout.Width(30)))
                {
                    _searchKeyWords.Add("");
                    _replaceKeyWords.Add("");
                }

                if (_searchKeyWords.Count > 2 && GUILayout.Button("-", GUILayout.Width(30)))
                {
                    _searchKeyWords.RemoveAt(_searchKeyWords.Count - 1);
                    _replaceKeyWords.RemoveAt(_replaceKeyWords.Count - 1);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Replace:", GUILayout.Width(50));
                for (int i = 0; i < _replaceKeyWords.Count; i++)
                {
                    bool isSearchEmpty = string.IsNullOrEmpty(_searchKeyWords[i]);
                    EditorGUI.BeginDisabledGroup(isSearchEmpty);
                    _replaceKeyWords[i] = EditorGUILayout.TextField(isSearchEmpty ? "" : _replaceKeyWords[i],
                        GUILayout.MinWidth(60));
                    EditorGUI.EndDisabledGroup();
                }

                GUILayout.Space(68);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            _findType = (FindType)EditorGUILayout.EnumPopup("Find Type", _findType);
            if (_findType == FindType.Subsequence)
            {
                _ignoreLineBreaks = EditorGUILayout.Toggle("Ignore Line Breaks", _ignoreLineBreaks);
            }

            _ignoreSpace = EditorGUILayout.Toggle("Ignore Space", _ignoreSpace);
            EditorGUILayout.EndHorizontal();

            if (_ignoreLineBreaks && _findType == FindType.Subsequence)
            {
                EditorGUILayout.HelpBox("Ignore Line Breaksが有効なため、行数は表示されず、置換は行えません", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find", GUILayout.Height(30)))
            {
                ExecuteSearch();
            }

            if (!(_ignoreLineBreaks && _findType == FindType.Subsequence))
            {
                EditorGUI.BeginDisabledGroup(results.Count == 0);
                if (GUILayout.Button("Replace All", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("警告", $"本当にすべての検索結果 ({results.Count}件) を置換しますか？\nこの操作は元に戻せません。",
                            "Yes", "No"))
                    {
                        ExecuteReplaceAll();
                    }
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Results: {results.Count}");

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = results.Count - 1; i >= 0; i--)
            {
                var result = results[i];
                EditorGUILayout.BeginVertical(GUI.skin.box);

                EditorGUILayout.BeginHorizontal();
                string label = result.LineNum > 0 ? $"Line {result.LineNum}: {result.Preview}" : result.Preview;
                EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);

                if (GUILayout.Button("Open", GUILayout.Width(50)))
                {
                    OpenInIDE(result);
                }

                if (GUILayout.Button("Replace", GUILayout.Width(60)))
                {
                    ExecuteReplaceSingle(result);
                    results.RemoveAt(i);
                    AssetDatabase.Refresh();
                }

                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    results.RemoveAt(i);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(result.Path, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Combine Files GUI

        private void DrawCombineFilesGUI()
        {
            EditorGUILayout.LabelField("Combine Files Mode", EditorStyles.boldLabel);
            _includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", _includeSubfolders);
            _ignoreSpace = EditorGUILayout.Toggle("Ignore Space", _ignoreSpace);
            _ignoreLineBreaks = EditorGUILayout.Toggle("Ignore Line Breaks", _ignoreLineBreaks);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Combine & Copy to Clipboard", GUILayout.Height(35)))
            {
                ExecuteCombineFiles();
            }
        }

        private void ExecuteCombineFiles()
        {
            if (!ValidateRootFolder()) return;

            try
            {
                var option = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                string searchPattern = "*" + _extension;
                var files = Directory.GetFiles(_rootFolder, searchPattern, option);

                StringBuilder sb = new StringBuilder();
                int count = 0;

                foreach (var path in files)
                {
                    if (Path.GetFileName(path) == "MultiFileSearchWindow.cs") continue;

                    string text = File.ReadAllText(path);

                    if (_ignoreSpace) text = DeleteSpace(text);
                    if (_ignoreLineBreaks) text = DeleteLineBreaks(text);

                    // ヘッダー情報としてファイル相対パスを付与
                    sb.AppendLine($"// ==========================================");
                    sb.AppendLine($"// File: {GetRelativePath(path)}");
                    sb.AppendLine($"// ==========================================");
                    sb.AppendLine(text);
                    sb.AppendLine();

                    count++;
                }

                GUIUtility.systemCopyBuffer = sb.ToString();
                EditorUtility.DisplayDialog("完了", $"{count} 件のファイルを結合し、クリップボードにコピーしました。", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"結合処理失敗: {e.Message}");
            }
        }

        #endregion

        #region Folder Structure GUI

        private void DrawFolderStructureGUI()
        {
            EditorGUILayout.LabelField("Folder Structure Output Mode", EditorStyles.boldLabel);
            _includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", _includeSubfolders);

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Generate & Copy Structure to Clipboard", GUILayout.Height(35)))
            {
                ExecuteGenerateFolderStructure();
            }
        }

        private void ExecuteGenerateFolderStructure()
        {
            if (!ValidateRootFolder()) return;

            try
            {
                StringBuilder sb = new StringBuilder();
                DirectoryInfo rootDir = new DirectoryInfo(_rootFolder);

                sb.AppendLine($"{rootDir.Name}/");
                BuildStructureTree(rootDir, "", sb, _includeSubfolders);

                GUIUtility.systemCopyBuffer = sb.ToString();
                EditorUtility.DisplayDialog("完了", "フォルダ構造を生成し、クリップボードにコピーしました。", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"構造生成失敗: {e.Message}");
            }
        }

        private void BuildStructureTree(DirectoryInfo dir, string indent, StringBuilder sb, bool recursive)
        {
            // 拡張子フィルタ適用後のファイル一覧を取得
            var files = string.IsNullOrEmpty(_extension)
                ? dir.GetFiles()
                : dir.GetFiles("*" + _extension);

            // 対象外ファイル(本スクリプト等)を除外
            files = files.Where(f => f.Name != "MultiFileSearchWindow.cs").ToArray();

            var subDirs = recursive ? dir.GetDirectories() : new DirectoryInfo[0];

            int totalCount = files.Length + subDirs.Length;
            int currentIndex = 0;

            // ディレクトリの出力
            foreach (var subDir in subDirs)
            {
                currentIndex++;
                bool isLast = currentIndex == totalCount;
                sb.AppendLine($"{indent}{(isLast ? "└── " : "├── ")}{subDir.Name}/");

                BuildStructureTree(subDir, indent + (isLast ? "    " : "│   "), sb, recursive);
            }

            // ファイルの出力
            foreach (var file in files)
            {
                currentIndex++;
                bool isLast = currentIndex == totalCount;
                sb.AppendLine($"{indent}{(isLast ? "└── " : "├── ")}{file.Name}");
            }
        }

        #endregion

        #region Helpers & Existing Search Logic

        private bool ValidateRootFolder()
        {
            if (string.IsNullOrEmpty(_rootFolder) || !Directory.Exists(_rootFolder))
            {
                EditorUtility.DisplayDialog("Error", "有効なルートフォルダを選択してください。", "OK");
                return false;
            }

            return true;
        }

        private string GetRelativePath(string fullPath)
        {
            if (fullPath.StartsWith(_rootFolder, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(_rootFolder.Length).TrimStart('\\', '/');
            }

            return fullPath;
        }

        private void OpenInIDE(SearchResult result)
        {
            string dataPath = UnityEngine.Application.dataPath;
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length);

            int lineNum = result.LineNum > 0 ? result.LineNum : 1;

            if (result.Path.Replace('\\', '/')
                .StartsWith(projectRoot.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                string assetRelativePath = result.Path.Substring(projectRoot.Length).Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetRelativePath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset, lineNum);
                    return;
                }
            }

            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(result.Path, lineNum, 0);
        }

        private void ExecuteSearch()
        {
            results.Clear();
            if (!ValidateRootFolder()) return;

            try
            {
                string searchPattern = "*" + _extension;
                var files = Directory.GetFiles(_rootFolder, searchPattern, SearchOption.AllDirectories);

                foreach (var path in files)
                {
                    if (Path.GetFileName(path) == "MultiFileSearchWindow.cs") continue;

                    if (_findType == FindType.Word)
                    {
                        SearchFileForWord(path);
                    }
                    else
                    {
                        SearchFileForSubsequence(path);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        private void SearchFileForWord(string filePath)
        {
            try
            {
                int lineNumber = 0;
                var fileText = File.ReadLines(filePath);

                foreach (string line in fileText)
                {
                    lineNumber++;
                    string text = _ignoreSpace ? DeleteSpace(line) : line;

                    if (!text.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new SearchResult
                    {
                        Path = filePath,
                        LineNum = lineNumber,
                        Preview = line.Trim(),
                        RawText = line
                    });
                }
            }
            catch
            {
            }
        }

        private void SearchFileForSubsequence(string filePath)
        {
            try
            {
                var validKeywords = _searchKeyWords.Where(k => !string.IsNullOrEmpty(k)).ToList();
                if (validKeywords.Count == 0) return;

                if (_ignoreLineBreaks)
                {
                    string fullText = File.ReadAllText(filePath);
                    string processedText = _ignoreSpace ? DeleteSpace(fullText) : fullText;
                    processedText = DeleteLineBreaks(processedText);

                    if (IsSubsequenceMatch(processedText, validKeywords))
                    {
                        results.Add(new SearchResult
                        {
                            Path = filePath,
                            LineNum = -1,
                            Preview = fullText.Length > 60 ? fullText.Substring(0, 60) + "..." : fullText,
                            RawText = fullText
                        });
                    }
                }
                else
                {
                    int lineNumber = 0;
                    var fileLines = File.ReadLines(filePath);

                    foreach (var line in fileLines)
                    {
                        lineNumber++;
                        string text = _ignoreSpace ? DeleteSpace(line) : line;

                        if (IsSubsequenceMatch(text, validKeywords))
                        {
                            results.Add(new SearchResult
                            {
                                Path = filePath,
                                LineNum = lineNumber,
                                Preview = line.Trim(),
                                RawText = line
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsSubsequenceMatch(string targetText, List<string> keywords)
        {
            int currentIndex = 0;
            foreach (var keyword in keywords)
            {
                int foundIndex = targetText.IndexOf(keyword, currentIndex, StringComparison.OrdinalIgnoreCase);
                if (foundIndex == -1) return false;
                currentIndex = foundIndex + keyword.Length;
            }

            return true;
        }

        private void ExecuteReplaceSingle(SearchResult result)
        {
            try
            {
                if (result.LineNum > 0)
                {
                    var lines = File.ReadAllLines(result.Path);
                    string originalLine = lines[result.LineNum - 1];
                    lines[result.LineNum - 1] = GetReplacedText(originalLine);
                    File.WriteAllLines(result.Path, lines);
                }
                else
                {
                    string fullText = File.ReadAllText(result.Path);
                    string replacedText = GetReplacedText(fullText);
                    File.WriteAllText(result.Path, replacedText);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"置換失敗: {result.Path}  {e.Message}");
            }
        }

        private void ExecuteReplaceAll()
        {
            var groupedResults = results.GroupBy(r => r.Path);

            foreach (var group in groupedResults)
            {
                try
                {
                    string filePath = group.Key;

                    if (_ignoreLineBreaks && _findType == FindType.Subsequence)
                    {
                        string fullText = File.ReadAllText(filePath);
                        fullText = GetReplacedText(fullText);
                        File.WriteAllText(filePath, fullText);
                    }
                    else
                    {
                        var lines = File.ReadAllLines(filePath);
                        foreach (var result in group.OrderByDescending(r => r.LineNum))
                        {
                            lines[result.LineNum - 1] = GetReplacedText(lines[result.LineNum - 1]);
                        }

                        File.WriteAllLines(filePath, lines);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"一括置換失敗: {group.Key}  {e.Message}");
                }
            }

            results.Clear();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完了", "一括置換が完了しました。", "OK");
        }

        private string GetReplacedText(string inputText)
        {
            if (_findType == FindType.Word)
            {
                return ReplaceStringString(inputText, _searchKeyword, _replaceKeyword,
                    StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return GetSubsequenceReplacedText(inputText);
            }
        }

        private string GetSubsequenceReplacedText(string inputText)
        {
            string result = "";
            int lastIndex = 0;

            while (true)
            {
                int searchPointer = lastIndex;
                List<(int start, int length, string replaceTo)> matchSegments = new();
                bool isFullMatch = true;

                for (int i = 0; i < _searchKeyWords.Count; i++)
                {
                    string keyword = _searchKeyWords[i];
                    if (string.IsNullOrEmpty(keyword)) continue;

                    int foundIndex = inputText.IndexOf(keyword, searchPointer, StringComparison.OrdinalIgnoreCase);
                    if (foundIndex == -1)
                    {
                        isFullMatch = false;
                        break;
                    }

                    matchSegments.Add((foundIndex, keyword.Length, _replaceKeyWords[i]));
                    searchPointer = foundIndex + keyword.Length;
                }

                if (!isFullMatch || matchSegments.Count == 0)
                {
                    result += inputText.Substring(lastIndex);
                    break;
                }

                int sequenceStart = matchSegments[0].start;
                result += inputText.Substring(lastIndex, sequenceStart - lastIndex);

                int currentSrcPointer = sequenceStart;
                foreach (var segment in matchSegments)
                {
                    if (segment.start > currentSrcPointer)
                    {
                        result += inputText.Substring(currentSrcPointer, segment.start - currentSrcPointer);
                    }

                    result += segment.replaceTo;
                    currentSrcPointer = segment.start + segment.length;
                }

                lastIndex = currentSrcPointer;
            }

            return result;
        }

        private string ReplaceStringString(string str, string oldValue, string newValue, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(oldValue)) return str;

            StringBuilder sb = new StringBuilder();
            int previousIndex = 0;
            int index = str.IndexOf(oldValue, comparison);

            while (index != -1)
            {
                sb.Append(str.Substring(previousIndex, index - previousIndex));
                sb.Append(newValue);
                previousIndex = index + oldValue.Length;
                index = str.IndexOf(oldValue, previousIndex, comparison);
            }

            sb.Append(str.Substring(previousIndex));
            return sb.ToString();
        }

        private string DeleteLineBreaks(string text)
        {
            return text.Replace("\r", "").Replace("\n", "");
        }

        private string DeleteSpace(string text)
        {
            return text.Replace(" ", "").Replace("\t", "");
        }

        #endregion
    }
}