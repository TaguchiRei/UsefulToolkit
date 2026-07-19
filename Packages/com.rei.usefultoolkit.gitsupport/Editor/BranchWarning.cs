using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UsefulToolkit.GitSupport
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
                        EditorUtility.DisplayDialog("警告", $"現在のブランチは[{currentBranch}]です。ブランチを切ってから作業してください", "OK");
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

            var currentBranch = BranchService.GetBranchName().ToLower();
            var warningBranch = setting.WarningBranches;

            bool save = true;

            if (warningBranch.Contains(currentBranch))
            {
                switch (setting.WarningType)
                {
                    case BranchWarningType.Warning:
                        save = EditorUtility.DisplayDialog("確認", "保存しますか？", "保存", "キャンセル");
                        break;
                    case BranchWarningType.CantSave:
                        save = false;
                        EditorUtility.DisplayDialog("警告", $"現在のブランチは[{currentBranch}]です。ブランチを切ってから作業してください", "OK");
                        break;
                    default:
                        //Noneの場合
                        break;
                }
            }

            return save ? paths : Array.Empty<string>();
        }
    }
}