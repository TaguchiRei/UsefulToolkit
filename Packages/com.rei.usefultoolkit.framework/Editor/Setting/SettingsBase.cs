using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.Setting
{
    /// <summary>
    /// UsefulToolkitSettingsのうち、個人単位で設定する物をこれで保存する
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [System.Serializable]
    public abstract class SettingsBase<T> where T : SettingsBase<T>, new()
    {
        private const string SaveDirectory = "UserSettings/UsefulToolkit";

        private static string SavePath =>
            Path.Combine(
                Application.persistentDataPath,
                SaveDirectory,
                $"{typeof(T).Name}.json"
            );

        public void Save()
        {
            var path = SavePath;
            var directory = Path.GetDirectoryName(path);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // EditorJsonUtility を使う。JsonUtility は [SerializeReference] フィールドを
            // 書き出さない/読み戻さないため、ポリモーフィック参照を持つ設定が永続化されない。
            var json = EditorJsonUtility.ToJson(this, true);
            File.WriteAllText(path, json);
        }

        public static T Load()
        {
            var path = SavePath;

            var result = new T();

            if (!File.Exists(path))
            {
                return result;
            }

            var json = File.ReadAllText(path);
            // FromJsonOverwrite は既存インスタンスへの上書きのみ対応。JSON に存在しない
            // フィールドは result の初期値のまま残る。
            EditorJsonUtility.FromJsonOverwrite(json, result);
            return result;
        }
    }
}