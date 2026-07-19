using System;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class GetComponentDataCommand : IGetCommand
    {
        public string Description => "【コンポーネント取得】指定したコンポーネント名のフィールド情報を取得します。引数(配列): [\"componentName\"]。※先にGetComponentDataCommand等でコンポーネント名を確認してください。";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"componentName\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected componentName argument.";
            string componentName = args[0];

            Type type = FindType(componentName);
            if (type == null) return $"Error: Component type '{componentName}' not found.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Component: {type.FullName}");

            var monoScripts = MonoImporter.GetAllRuntimeMonoScripts();
            foreach (var script in monoScripts)
            {
                if (script.GetClass() == type)
                {
                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    if (AIBlackList.Instance.IsBlacklisted(scriptPath))
                    {
                        sb.AppendLine("Script Path: [RESTRICTED]");
                    }
                    else
                    {
                        sb.AppendLine($"Script Path: {scriptPath}");
                    }
                    break;
                }
            }

            sb.AppendLine("Fields:");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var field in fields)
            {
                if (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
                {
                    sb.AppendLine($"- {field.Name} ({field.FieldType.Name})");
                }
            }

            return sb.ToString();
        }

        private Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(typeName);
                if (t != null) return t;
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in assembly.GetTypes())
                {
                    if (t.Name == typeName) return t;
                }
            }
            return null;
        }
    }

    [Serializable]
    public class ChangeInspectorCommand : ISetCommand
    {
        public string Description =>
            "【値変更】指定したGameObjectのアクセストークンを用いて、コンポーネントのフィールド値を変更します。引数(配列): [\"accessToken\", \"componentName\", \"fieldName\", \"value\"]。※アクセストークンはGetSceneObjectCommand等で取得してください。";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return
                    "Error: Invalid argument format. Expected [\"accessToken\", \"componentName\", \"fieldName\", \"value\"].";
            }

            if (args == null || args.Length < 4)
                return "Error: Expected [accessToken, componentName, fieldName, value].";

            string accessToken = args[0];
            string componentName = args[1];
            string fieldName = args[2];
            string value = args[3];

            if (!ISetCommand.GetFromAccessToken(accessToken, out string path))
            {
                return "Error: Invalid AccessToken.";
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Error: Invalid target path.";
            }

            if (AIBlackList.Instance.IsBlacklisted(path))
                return $"Error: Access denied. Target '{path}' is blacklisted.";

            GameObject target = GameObject.Find(path);
            if (target == null) return $"Error: GameObject at path '{path}' not found.";

            Component component = target.GetComponent(componentName);
            if (component == null) return $"Error: Component '{componentName}' not found on '{target.name}'.";

            Type type = component.GetType();
            FieldInfo field = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return $"Error: Field '{fieldName}' not found on '{componentName}'.";

            if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
            {
                return $"Error: Field '{fieldName}' is not accessible.";
            }

            try
            {
                object convertedValue = ConvertValue(value, field.FieldType);
                field.SetValue(component, convertedValue);
                EditorUtility.SetDirty(component);
                return $"Success: Changed {fieldName} on {componentName} to {value}.";
            }
            catch (Exception e)
            {
                return $"Error: Failed to change value. {e.Message}";
            }
        }

        private object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(float)) return float.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                if (!ISetCommand.GetFromAccessToken(value, out string targetPath))
                {
                    throw new InvalidOperationException(
                        "UnityEngine.Object references require a valid access token.");
                }

                GameObject go = GameObject.Find(targetPath);

                if (go != null)
                {
                    if (targetType == typeof(GameObject))
                    {
                        return go;
                    }

                    Component component = go.GetComponent(targetType);

                    if (component != null)
                    {
                        return component;
                    }
                }

                UnityEngine.Object asset =
                    AssetDatabase.LoadAssetAtPath(targetPath, targetType);

                if (asset != null)
                {
                    return asset;
                }

                throw new InvalidOperationException(
                    $"Target object not found. Token resolved to '{targetPath}'.");
            }

            return Convert.ChangeType(value, targetType);
        }
    }

    [Serializable]
    public class ApplyPrefabOverridesCommand : ISetCommand
    {
        public string Description => "【プレハブ適用】シーン上のGameObjectの変更を、そのプレハブアセットに適用します。引数(配列): [\"gameObjectPath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try { args = JsonConvert.DeserializeObject<string[]>(argument); }
            catch { return "Error: Invalid argument format."; }

            if (args == null || args.Length < 1) return "Error: Expected gameObjectPath argument.";
            string goPath = args[0];

            if (AIBlackList.Instance.IsBlacklisted(goPath))
                return $"Error: Access denied. GameObject '{goPath}' is blacklisted.";

            GameObject go = GameObject.Find(goPath);
            if (go == null) return $"Error: GameObject '{goPath}' not found in scene.";

            if (!PrefabUtility.IsPartOfAnyPrefab(go))
                return $"Error: GameObject '{go.name}' is not a prefab instance.";

            try
            {
                GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                string assetPath = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root));
                if (AIBlackList.Instance.IsBlacklisted(assetPath))
                    return $"Error: Access denied. Prefab asset '{assetPath}' is blacklisted.";

                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
                return $"Success: Applied overrides from {root.name} to prefab at {assetPath}.";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }
}
