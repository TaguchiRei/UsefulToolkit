using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UsefulToolkit.Editor.Utility
{
    /// <summary>
    /// ダイアログ・ファイル選択パネルの呼び出しを包む。
    /// <see cref="AiMode.Enabled"/> が false なら Unity の標準ダイアログをそのまま出し、
    /// true ならダイアログを出さずに、<see cref="AiMode"/> に登録された回答か呼び出し側の既定値を使ってログへ結果を出す。
    /// </summary>
    public static class EditorPrompt
    {
        private const string LogPrefix = "[UsefulToolkit] AIモード";

        /// <summary>
        /// OKボタンだけのダイアログを出す。AIモードでは指定したログ種別でコンソールへ出す。
        /// </summary>
        /// <param name="title">タイトル</param>
        /// <param name="message">本文</param>
        /// <param name="logType">AIモードで出すログの種別</param>
        public static void Notify(string title, string message, LogType logType = LogType.Log)
        {
            if (!AiMode.Enabled)
            {
                EditorUtility.DisplayDialog(title, message, "OK");
                return;
            }

            Debug.unityLogger.Log(logType, $"{LogPrefix} : 「{title}」\n{message}");
        }

        /// <summary>
        /// 2択の確認ダイアログを出す。AIモードでは回答が登録されていればそれを、無ければ既定値を使う。
        /// </summary>
        /// <param name="key">AIモードで回答を引くキー</param>
        /// <param name="title">タイトル</param>
        /// <param name="message">本文</param>
        /// <param name="ok">OKボタンの表示名</param>
        /// <param name="cancel">キャンセルボタンの表示名</param>
        /// <param name="aiDefault">AIモードで回答が無いときの結果</param>
        /// <returns>OKが選ばれた場合はtrue</returns>
        public static bool Confirm(string key, string title, string message, string ok, string cancel, bool aiDefault)
        {
            if (!AiMode.Enabled)
            {
                return EditorUtility.DisplayDialog(title, message, ok, cancel);
            }

            bool result = aiDefault;
            string source = "既定値";

            if (AiMode.TryGetAnswer(key, out string answer))
            {
                if (TryParseConfirmAnswer(answer, ok, cancel, out bool parsed))
                {
                    result = parsed;
                    source = "登録済みの回答";
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} : キー [{key}] の回答 [{answer}] を解釈できないため既定値を使います。" +
                                     $"[{ok}] / [{cancel}] / true / false のいずれかを指定してください。");
                }
            }

            Debug.Log($"{LogPrefix} : 「{title}」→ [{(result ? ok : cancel)}]({source} / キー [{key}])\n{message}");
            return result;
        }

        /// <summary>
        /// 3択のダイアログを出す。戻り値の対応は <see cref="EditorUtility.DisplayDialogComplex"/> と同じ。
        /// AIモードではボタンの表示名で登録された回答を、無ければ既定値を使う。
        /// </summary>
        /// <param name="key">AIモードで回答を引くキー</param>
        /// <param name="title">タイトル</param>
        /// <param name="message">本文</param>
        /// <param name="ok">0番のボタンの表示名</param>
        /// <param name="cancel">1番のボタンの表示名</param>
        /// <param name="alt">2番のボタンの表示名</param>
        /// <param name="aiDefault">AIモードで回答が無いときに選ぶボタンの番号</param>
        /// <returns>選ばれたボタンの番号。0 : ok / 1 : cancel / 2 : alt</returns>
        public static int Choose(
            string key, string title, string message, string ok, string cancel, string alt, int aiDefault)
        {
            if (!AiMode.Enabled)
            {
                return EditorUtility.DisplayDialogComplex(title, message, ok, cancel, alt);
            }

            string[] labels = { ok, cancel, alt };
            int result = aiDefault;
            string source = "既定値";

            if (AiMode.TryGetAnswer(key, out string answer))
            {
                int index = Array.IndexOf(labels, answer);
                if (index >= 0)
                {
                    result = index;
                    source = "登録済みの回答";
                }
                else
                {
                    Debug.LogWarning($"{LogPrefix} : キー [{key}] の回答 [{answer}] を解釈できないため既定値を使います。" +
                                     $"[{ok}] / [{cancel}] / [{alt}] のいずれかを指定してください。");
                }
            }

            Debug.Log($"{LogPrefix} : 「{title}」→ [{labels[result]}]({source} / キー [{key}])\n{message}");
            return result;
        }

        /// <summary>
        /// プロジェクト内の保存先ファイルを選ばせる。戻り値の形式は
        /// <see cref="EditorUtility.SaveFilePanelInProject(string,string,string,string)"/> と同じ。
        /// AIモードでは回答が必須で、無い場合や Assets 配下の既存フォルダを指さない場合は空文字を返す。
        /// </summary>
        /// <param name="key">AIモードで回答を引くキー</param>
        /// <param name="title">タイトル</param>
        /// <param name="defaultName">既定のファイル名</param>
        /// <param name="extension">拡張子(ドットなし)</param>
        /// <param name="message">説明文</param>
        /// <returns>"Assets/" から始まる保存先のパス。キャンセル時は空文字</returns>
        public static string SaveFilePanelInProject(
            string key, string title, string defaultName, string extension, string message)
        {
            if (!AiMode.Enabled)
            {
                return EditorUtility.SaveFilePanelInProject(title, defaultName, extension, message);
            }

            if (!TryGetRequiredAnswer(key, title, $"Assets/.../{defaultName}.{extension}", out string path))
            {
                return string.Empty;
            }

            path = path.Replace('\\', '/');
            if (!path.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
            {
                path += "." + extension;
            }

            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(directory))
            {
                Debug.LogError($"{LogPrefix} : キー [{key}] の回答 [{path}] は Assets 配下の既存フォルダを指していません。" +
                               "フォルダを作成するか、回答を修正してから再実行してください。");
                return string.Empty;
            }

            Debug.Log($"{LogPrefix} : 「{title}」→ [{path}](登録済みの回答 / キー [{key}])");
            return path;
        }

        /// <summary>
        /// フォルダを選ばせる。AIモードでは回答が必須で、無い場合は空文字を返す。
        /// 回答はプロジェクトルートからの相対パス("Assets/...")でも絶対パスでもよい。
        /// </summary>
        /// <param name="key">AIモードで回答を引くキー</param>
        /// <param name="title">タイトル</param>
        /// <param name="folder">初期表示するフォルダ</param>
        /// <param name="defaultName">既定のフォルダ名</param>
        /// <returns>選ばれたフォルダのパス。キャンセル時は空文字</returns>
        public static string OpenFolderPanel(string key, string title, string folder, string defaultName)
        {
            if (!AiMode.Enabled)
            {
                return EditorUtility.OpenFolderPanel(title, folder, defaultName);
            }

            if (!TryGetRequiredAnswer(key, title, "Assets/...", out string path))
            {
                return string.Empty;
            }

            Debug.Log($"{LogPrefix} : 「{title}」→ [{path}](登録済みの回答 / キー [{key}])");
            return path;
        }

        /// <summary>
        /// 変更のある開いているシーンを保存してよいか確かめる。
        /// AIモードでは確認せずに保存する。ただし保存先の決まっていない変更済みシーンがあると
        /// 保存ダイアログが出てしまうため、保存せずに false を返す。
        /// </summary>
        /// <returns>続行してよい場合はtrue</returns>
        public static bool SaveModifiedScenes()
        {
            if (!AiMode.Enabled)
            {
                return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty && string.IsNullOrEmpty(scene.path))
                {
                    Debug.LogError($"{LogPrefix} : 保存先の決まっていない変更済みシーン [{scene.name}] があるため中止しました。" +
                                   "EditorSceneManager.SaveScene(scene, path) で保存してから再実行してください。");
                    return false;
                }
            }

            if (!EditorSceneManager.SaveOpenScenes())
            {
                Debug.LogError($"{LogPrefix} : 開いているシーンを保存できなかったため中止しました。");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 必須の回答を取得する。未登録の場合は登録方法をエラーログへ出す。
        /// </summary>
        /// <param name="key">回答のキー</param>
        /// <param name="title">ダイアログのタイトル</param>
        /// <param name="example">回答の例</param>
        /// <param name="answer">登録済みの回答</param>
        /// <returns>登録されていればtrue</returns>
        private static bool TryGetRequiredAnswer(string key, string title, string example, out string answer)
        {
            if (AiMode.TryGetAnswer(key, out answer))
            {
                return true;
            }

            Debug.LogError($"{LogPrefix} : 「{title}」の入力が必要なため中止しました。" +
                           $"AiMode.SetAnswer(\"{key}\", \"{example}\") で指定してから再実行してください。");
            return false;
        }

        /// <summary>
        /// 確認ダイアログの回答を解釈する。ボタンの表示名か true/false を受け付ける。
        /// </summary>
        private static bool TryParseConfirmAnswer(string answer, string ok, string cancel, out bool result)
        {
            if (answer == ok)
            {
                result = true;
                return true;
            }

            if (answer == cancel)
            {
                result = false;
                return true;
            }

            return bool.TryParse(answer, out result);
        }
    }
}
