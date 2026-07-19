using System.IO;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    public class LocalSettingBase<T> : ScriptableObject where T : ScriptableObject
    {
        protected static T _instance;
        private static string AssetPath => Path.Combine(FileGenerator.GenerateLocalRootPath, $"{typeof(T).Name}.asset");
        
        public static T Load()
        {
            if (_instance != null) return _instance;
            
            _instance = AssetDatabase.LoadAssetAtPath<T>(AssetPath);

            if (_instance == null)
            {
                _instance = FileGenerator.AutoGenerateAsset<T>(typeof(T).Name, GenerateType.Editor);
            }

            return _instance;
        }
    }
}
