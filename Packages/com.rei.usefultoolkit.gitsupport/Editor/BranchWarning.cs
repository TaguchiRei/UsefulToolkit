using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UsefulToolkit.Editor.Utility;

namespace UsefulToolkit.Editor.GitSupport
{
    //TODO : 今後Githubの機能とも連携させて誰でもGit警告設定を変えられなくするなどしてより堅牢な仕組みにする
    [InitializeOnLoad]
    public class BranchWarning : AssetModificationProcessor
    {
        static BranchWarning()
        {
            CompilationPipeline.compilationFinished += OnCompiled;
        }

        public static void OnCompiled(object obj)
        {
            var setting = GitSupportSettings.Load();

            //設定でコンパイル時のブランチチェックを切っている場合はそのまま通す
            if (!setting.WarningOnCompiled)
            {
                return;
            }

            var currentBranch = BranchService.GetBranchName().ToLower();
            var warningBranch = setting.WarningBranches;

            if (warningBranch.Contains(currentBranch))
            {
                switch (setting.WarningType)
                {
                    case BranchWarningType.None:
                        break;
                    default:
                        EditorPrompt.Notify("警告", $"現在のブランチは[{currentBranch}]です。ブランチを切ってから作業してください", LogType.Warning);
                        break;
                }
            }
        }

        static string[] OnWillSaveAssets(string[] paths)
        {
            var setting = GitSupportSettings.Load();

            //設定でセーブ時のブランチチェックを切っている場合はそのまま通す
            if (!setting.WarningOnSaved)
            {
                return paths;
            }

            //Assets以下の保存が含まれない場合はUnityによる自動保存なのでそのまま通す
            if (!Array.Exists(paths, IsUnderAssets))
            {
                return paths;
            }

            var currentBranch = BranchService.GetBranchName().ToLower();
            var warningBranch = setting.WarningBranches;

            bool save = true;

            if (warningBranch.Contains(currentBranch))
            {
                switch (setting.WarningType)
                {
                    case BranchWarningType.Warning:
                        save = EditorPrompt.Confirm("GitSupport.SaveOnWarningBranch", "確認",
                            $"現在のブランチは[{currentBranch}]です。保存しますか？", "保存", "キャンセル", false);
                        break;
                    case BranchWarningType.CantSave:
                        save = false;
                        EditorPrompt.Notify("警告", $"現在のブランチは[{currentBranch}]です。ブランチを切ってから作業してください", LogType.Warning);
                        break;
                    default:
                        //Noneの場合
                        break;
                }
            }

            //保存しない場合もAssets以下以外のパスは保存対象に残す
            return save ? paths : Array.FindAll(paths, path => !IsUnderAssets(path));
        }

        /// <summary>
        /// パスがAssetsフォルダ以下を指しているかを返す
        /// </summary>
        private static bool IsUnderAssets(string path)
        {
            return path.StartsWith("Assets/", StringComparison.Ordinal);
        }
    }
}