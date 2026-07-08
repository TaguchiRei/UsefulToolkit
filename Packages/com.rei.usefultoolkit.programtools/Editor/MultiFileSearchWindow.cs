using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.ProgramTools.Search
{
    using Debug = UnityEngine.Debug;

    public class MultiFileSearchWindow : EditorWindow
    {
        [Serializable]
        private class SearchResult
        {
            public string Path;
            public int LineNum;

            public string Preview;

            // 置換処理のためにマッチした行の文字列（またはファイル全体）を保持
            public string RawText;
        }

        private enum FindType
        {
            Word,
            Subsequence
        }

        [SerializeField] private string _searchKeyword = "";
        [SerializeField] private string _replaceKeyword = ""; // Wordモード用の置換文字列
        [SerializeField] private string _extension = ".cs";
        [SerializeField] private string _rootFolder = "";
        [SerializeField] private bool _ignoreLineBreaks = false;
        [SerializeField] private bool _ignoreSpace = false;
        [SerializeField] private FindType _findType = FindType.Word;

        // 部分列検索用のキーワードリストと、それに対応する置換文字列リスト
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
            // 検索・置換キーワード入力部 
            if (_findType == FindType.Word)
            {
                EditorGUILayout.LabelField("Word Search Mode");
                _searchKeyword = EditorGUILayout.TextField("Search Keyword", _searchKeyword);
                _replaceKeyword = EditorGUILayout.TextField("Replace Keyword", _replaceKeyword);
            }
            else
            {
                EditorGUILayout.LabelField("Subsequence Search Mode");

                // 検索キーワード行
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

                // 置換キーワード行
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Replace:", GUILayout.Width(50));
                for (int i = 0; i < _replaceKeyWords.Count; i++)
                {
                    // 検索側が空欄（ワイルドカード）の場所は置換入力できないように制御
                    bool isSearchEmpty = string.IsNullOrEmpty(_searchKeyWords[i]);
                    EditorGUI.BeginDisabledGroup(isSearchEmpty);
                    _replaceKeyWords[i] = EditorGUILayout.TextField(isSearchEmpty ? "" : _replaceKeyWords[i],
                        GUILayout.MinWidth(60));
                    EditorGUI.EndDisabledGroup();
                }

                GUILayout.Space(68);
                EditorGUILayout.EndHorizontal();
            }

            // 共通設定部 
            _extension = EditorGUILayout.TextField("Extension", _extension);

            EditorGUILayout.BeginHorizontal();
            _rootFolder = EditorGUILayout.TextField("Root Folder", _rootFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string defaultPath = string.IsNullOrEmpty(_rootFolder) ? Application.dataPath : _rootFolder;
                _rootFolder = EditorUtility.OpenFolderPanel("Select Root Folder", defaultPath, "");
            }

            EditorGUILayout.EndHorizontal();

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

            // 検索・一括置換ボタン 
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find", GUILayout.Height(30)))
            {
                ExecuteSearch();
            }

            if (_ignoreLineBreaks && _findType == FindType.Subsequence)
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

            // 結果表示部 
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Results: {results.Count}");

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = results.Count - 1; i >= 0; i--) // 置換によるインデックスずれを防ぐため
            {
                var result = results[i];
                EditorGUILayout.BeginVertical(GUI.skin.box);

                EditorGUILayout.BeginHorizontal();
                string label = result.LineNum > 0 ? $"Line {result.LineNum}: {result.Preview}" : result.Preview;
                EditorGUILayout.LabelField(label, EditorStyles.wordWrappedLabel);

                // IDEで開くボタン
                if (GUILayout.Button("Open", GUILayout.Width(50)))
                {
                    OpenInIDE(result);
                }

                // 個別置換ボタン
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

        /// <summary>
        /// 検索結果をIDEで開く。
        /// Assetsフォルダ配下のファイルはUnityのAssetDatabase経由で開き、行番号へジャンプする。
        /// Assets外のファイルはUnityEditorInternal.InternalEditorUtility経由で直接開く。
        /// </summary>
        private void OpenInIDE(SearchResult result)
        {
            // パスをAssetsからの相対パスに変換できるか試みる
            string dataPath = Application.dataPath; // 例: /path/to/Project/Assets
            string projectRoot = dataPath.Substring(0, dataPath.Length - "Assets".Length); // /path/to/Project/

            int lineNum = result.LineNum > 0 ? result.LineNum : 1;

            if (result.Path.Replace('\\', '/')
                .StartsWith(projectRoot.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                // Assets配下 → AssetDatabaseで開いてから行番号へジャンプ
                string assetRelativePath = result.Path.Substring(projectRoot.Length).Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetRelativePath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset, lineNum);
                    return;
                }
            }

            // Assets外、またはAssetDatabase経由で開けなかった場合はInternalEditorUtilityで直接開く
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(result.Path, lineNum, 0);
        }

        private void ExecuteSearch()
        {
            results.Clear();
            if (string.IsNullOrEmpty(_rootFolder) || !Directory.Exists(_rootFolder))
            {
                EditorUtility.DisplayDialog("Error", "有効なルートフォルダを選択してください。", "OK");
                return;
            }

            try
            {
                string searchPattern = "*" + _extension;
                var files = Directory.GetFiles(_rootFolder, searchPattern, SearchOption.AllDirectories);

                foreach (var path in files)
                {
                    if (Path.GetFileName(path) == "MultiFileSearchWindow.cs")
                    {
                        continue;
                    }

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
                        RawText = line // 行置換用に元の文字列を保持
                    });
                }
            }
            catch
            {
                //読み取れないファイルは無視する
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
                            RawText = fullText // ファイル全体置換用に保持
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
                                RawText = line // 行置換用に保持
                            });
                        }
                    }
                }
            }
            catch
            {
                // 読み取れないファイルは無視する
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

        // 置換ロジック部 

        private void ExecuteReplaceSingle(SearchResult result)
        {
            try
            {
                if (result.LineNum > 0)
                {
                    // 行単位の置換
                    var lines = File.ReadAllLines(result.Path);
                    // インデックスは0始まりなので -1
                    string originalLine = lines[result.LineNum - 1];
                    lines[result.LineNum - 1] = GetReplacedText(originalLine);
                    File.WriteAllLines(result.Path, lines);
                }
                else
                {
                    // ファイル全体の置換（Ignore Line Breaks時）
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
            // 同じファイルに対する複数行の置換に対応するため、ファイルパスごとにグルーピングして処理
            var groupedResults = results.GroupBy(r => r.Path);

            foreach (var group in groupedResults)
            {
                try
                {
                    string filePath = group.Key;

                    if (_ignoreLineBreaks && _findType == FindType.Subsequence)
                    {
                        // 改行無視モードはファイル全体を一発置換
                        string fullText = File.ReadAllText(filePath);
                        fullText = GetReplacedText(fullText);
                        File.WriteAllText(filePath, fullText);
                    }
                    else
                    {
                        // 行単位モード
                        var lines = File.ReadAllLines(filePath);
                        // 行番号が大きい方から置換
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
            AssetDatabase.Refresh(); // Unityエディタにファイルの変更を通知
            EditorUtility.DisplayDialog("完了", "一括置換が完了しました。", "OK");
        }

        /// <summary>
        /// 対象テキストをWordまたはSubsequenceルールに基づいて置換した文字列を返す
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns></returns>
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

                // 1セットの部分列のペアがすべて見つかるか走査
                for (int i = 0; i < _searchKeyWords.Count; i++)
                {
                    string keyword = _searchKeyWords[i];
                    if (string.IsNullOrEmpty(keyword)) continue; // 空欄はスキップ

                    int foundIndex = inputText.IndexOf(keyword, searchPointer, StringComparison.OrdinalIgnoreCase);
                    if (foundIndex == -1)
                    {
                        isFullMatch = false;
                        break;
                    }

                    matchSegments.Add((foundIndex, keyword.Length, _replaceKeyWords[i]));
                    searchPointer = foundIndex + keyword.Length;
                }

                // これ以上部分列が見つからなければ終了
                if (!isFullMatch || matchSegments.Count == 0)
                {
                    result += inputText.Substring(lastIndex);
                    break;
                }

                // 結果に結合
                int sequenceStart = matchSegments[0].start;
                result += inputText.Substring(lastIndex, sequenceStart - lastIndex);

                // 各キーワード部分を新しい文字列に置き換え、間の部分を維持しながら合成
                int currentSrcPointer = sequenceStart;
                foreach (var segment in matchSegments)
                {
                    // キーワードの直前にある、維持すべき文字列を切り出して結合
                    if (segment.start > currentSrcPointer)
                    {
                        result += inputText.Substring(currentSrcPointer, segment.start - currentSrcPointer);
                    }

                    // 置換文字列を結合
                    result += segment.replaceTo;
                    currentSrcPointer = segment.start + segment.length;
                }

                lastIndex = currentSrcPointer;
            }

            return result;
        }

        // 大文字小文字を無視した文字列置換用ヘルパー
        private string ReplaceStringString(string str, string oldValue, string newValue, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(oldValue)) return str;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
    }
}