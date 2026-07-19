using System.IO;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// UsefulToolkitSettingsのうち、個人単位で設定する物をこれで保存する
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [System.Serializable]
    public abstract class SettingsBase<T> where T : SettingsBase<T>, new()
    {
        private const string SaveDirectory = "UserSettings/UsefulToolkit";

        private static string SavePath
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    SaveDirectory,
                    $"{typeof(T).Name}.json"
                );
            }
        }

        public void Save()
        {
            var path = SavePath;
            var directory = Path.GetDirectoryName(path);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonUtility.ToJson(this, true);
            File.WriteAllText(path, json);
        }

        public static T Load()
        {
            var path = SavePath;

            if (!File.Exists(path))
            {
                return new T();
            }

            var json = File.ReadAllText(path);
            return JsonUtility.FromJson<T>(json);
        }
    }
}