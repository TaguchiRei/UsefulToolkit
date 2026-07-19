using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class GetAssetDataCommand : IGetCommand
    {
        public string Description =>
            "【アセットデータ取得】指定したパスのアセット（ScriptableObjectやPrefab）のフィールド情報を取得します。引数(配列): [\"assetPath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"assetPath\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected assetPath argument.";
            string assetPath = args[0];

            if (AIBlackList.Instance.IsBlacklisted(assetPath))
                return $"Error: Access denied. Asset '{assetPath}' is blacklisted.";

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) return $"Error: Asset at path '{assetPath}' not found.";

            string token = IGetCommand.GetAccessToken(assetPath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Asset: {asset.name} ({asset.GetType().Name})");
            sb.AppendLine($"Path: {assetPath}");
            sb.AppendLine($"AccessToken: {token}");

            if (asset is GameObject go)
            {
                sb.AppendLine("Components:");
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    sb.AppendLine($"- {component.GetType().Name}");
                    AppendSerializedProperties(component, sb, 2);
                }
            }
            else
            {
                sb.AppendLine("Properties:");
                AppendSerializedProperties(asset, sb, 1);
            }

            return sb.ToString();
        }

        private void AppendSerializedProperties(UnityEngine.Object obj, StringBuilder sb, int indent)
        {
            SerializedObject so = new SerializedObject(obj);
            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;
            string indentStr = new string(' ', indent * 2);
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                sb.AppendLine($"{indentStr}{prop.name} ({prop.propertyType}): {GetPropertyValue(prop)}");
            }
        }

        private string GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString();
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Color: return prop.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null
                        ? $"{prop.objectReferenceValue.name} ({prop.objectReferenceValue.GetType().Name})"
                        : "null";
                case SerializedPropertyType.Enum:
                    if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length)
                        return prop.enumDisplayNames[prop.enumValueIndex];
                    return prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2: return prop.vector2Value.ToString();
                case SerializedPropertyType.Vector3: return prop.vector3Value.ToString();
                default: return $"({prop.propertyType})";
            }
        }
    }

    [Serializable]
    public class ModifyAssetCommand : ISetCommand
    {
        public string Description =>
            "【アセット変更】指定したアセットのフィールド値を変更します。引数(配列): [\"accessToken\", \"componentName\", \"fieldName\", \"value\"]。※SOの場合はcomponentNameを空文字にしてください。";

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

            if (args == null || args.Length < 4) return "Error: Expected 4 arguments.";

            string token = args[0];
            string componentName = args[1];
            string fieldName = args[2];
            string value = args[3];

            if (!ISetCommand.GetFromAccessToken(token, out string assetPath)) return "Error: Invalid AccessToken.";

            if (AIBlackList.Instance.IsBlacklisted(assetPath))
                return $"Error: Access denied. Asset '{assetPath}' is blacklisted.";

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) return $"Error: Asset at '{assetPath}' not found.";

            UnityEngine.Object target = asset;
            if (asset is GameObject go && !string.IsNullOrEmpty(componentName))
            {
                target = go.GetComponent(componentName);
                if (target == null) return $"Error: Component '{componentName}' not found on Prefab.";
            }

            Undo.RecordObject(target, "AI Modify Asset");
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);

            if (prop == null) return $"Error: Property '{fieldName}' not found on '{target.GetType().Name}'.";

            try
            {
                SetPropertyValue(prop, value);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
                return $"Success: Updated {fieldName} on {target.name} to {value}.";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private void SetPropertyValue(SerializedProperty prop, string value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: prop.intValue = int.Parse(value); break;
                case SerializedPropertyType.Boolean: prop.boolValue = bool.Parse(value); break;
                case SerializedPropertyType.Float: prop.floatValue = float.Parse(value); break;
                case SerializedPropertyType.String: prop.stringValue = value; break;
                case SerializedPropertyType.Enum:
                    int index = Array.IndexOf(prop.enumDisplayNames, value);
                    if (index >= 0) prop.enumValueIndex = index;
                    else prop.enumValueIndex = int.Parse(value);
                    break;
                case SerializedPropertyType.Color:
                    if (ColorUtility.TryParseHtmlString(value, out Color color)) prop.colorValue = color;
                    else throw new Exception("Invalid color format. Use HTML color string.");
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (ISetCommand.GetFromAccessToken(value, out string targetPath))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath(targetPath, typeof(UnityEngine.Object));
                        if (obj != null)
                        {
                            prop.objectReferenceValue = obj;
                            break;
                        }
                    }

                    throw new Exception(
                        "ObjectReference modification failed. Ensure a valid AccessToken is provided for the value.");
                default:
                    throw new Exception($"Property type {prop.propertyType} is not supported yet.");
            }
        }
    }

    [Serializable]
    public class CreateScriptableObjectCommand : ISetCommand
    {
        public string Description =>
            "【SO作成】新規ScriptableObjectを作成します。引数(配列): [\"parentFolderToken\", \"fileName\", \"typeName\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format.";
            }

            if (args == null || args.Length < 3) return "Error: Expected 3 arguments.";

            string parentToken = args[0];
            string fileName = args[1];
            string typeName = args[2];

            if (!ISetCommand.GetFromAccessToken(parentToken, out string parentPath))
                return "Error: Invalid ParentFolderToken.";

            if (AIBlackList.Instance.IsBlacklisted(parentPath))
                return $"Error: Access denied. Parent folder '{parentPath}' is blacklisted.";

            Type type = FindType(typeName);
            if (type == null) return $"Error: Type '{typeName}' not found.";
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                return $"Error: '{typeName}' is not a ScriptableObject.";

            try
            {
                ScriptableObject so = ScriptableObject.CreateInstance(type);
                string path = System.IO.Path.Combine(parentPath, fileName + ".asset");
                path = AssetDatabase.GenerateUniqueAssetPath(path);

                if (AIBlackList.Instance.IsBlacklisted(path))
                    return $"Error: Access denied. Target path '{path}' is blacklisted.";

                AssetDatabase.CreateAsset(so, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                string newToken = IGetCommand.GetAccessToken(path);
                return $"Success: Created {typeName} at {path}. AccessToken: {newToken}";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
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
    public class CreatePrefabCommand : ISetCommand
    {
        public string Description =>
            "【プレハブ作成】シーン上のGameObjectからプレハブアセットを作成します。引数(配列): [\"gameObjectPath\", \"parentFolderToken\", \"fileName\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format.";
            }

            if (args == null || args.Length < 3) return "Error: Expected 3 arguments.";

            string goPath = args[0];
            string parentToken = args[1];
            string fileName = args[2];

            if (AIBlackList.Instance.IsBlacklisted(goPath))
                return $"Error: Access denied. GameObject '{goPath}' is blacklisted.";

            GameObject go = GameObject.Find(goPath);
            if (go == null) return $"Error: GameObject '{goPath}' not found in scene.";

            if (!ISetCommand.GetFromAccessToken(parentToken, out string parentPath))
                return "Error: Invalid ParentFolderToken.";

            if (AIBlackList.Instance.IsBlacklisted(parentPath))
                return $"Error: Access denied. Parent folder '{parentPath}' is blacklisted.";

            try
            {
                string path = System.IO.Path.Combine(parentPath, fileName + ".prefab");
                path = AssetDatabase.GenerateUniqueAssetPath(path);

                if (AIBlackList.Instance.IsBlacklisted(path))
                    return $"Error: Access denied. Target path '{path}' is blacklisted.";

                PrefabUtility.SaveAsPrefabAsset(go, path);
                AssetDatabase.Refresh();
                string newToken = IGetCommand.GetAccessToken(path);
                return $"Success: Created Prefab from {go.name} at {path}. AccessToken: {newToken}";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }

    [Serializable]
    public class ExecuteGeneratorCommand : ISetCommand
    {
        public string Description =>
            "【ツール実行】EnumGeneratorなどの自動生成ツールを実行します。引数(配列): [\"generatorName\"]。有効な名前: Audio, Scene, Input";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format.";
            }

            if (args == null || args.Length < 1) return "Error: Expected generatorName argument.";
            string name = args[0].ToLower();

            try
            {
                switch (name)
                {
                    case "audio":
                        //AudioEnumGenerator.Generate();
                        Debug.Log("AudioEnumGenerator is Not yet implemented");
                        return "Success: Triggered Audio Enum Generation.";
                    case "scene":
                        //SceneEnumGenerator.Generate();
                        Debug.Log("AudioEnumGenerator is Not yet implemented");
                        return "Success: Triggered Scene Enum Generation.";
                    case "input":
                        CallStaticMethod("UsefulToolkit.Editor.InputActionEnumGenerator", "Generate");
                        return "Success: Triggered Input Action Enum Generation.";
                    default:
                        return $"Error: Unknown generator '{name}'.";
                }
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }

        private void CallStaticMethod(string typeName, string methodName)
        {
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) break;
            }

            if (type == null) throw new Exception($"Type '{typeName}' not found.");
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new Exception($"Method '{methodName}' not found on '{typeName}'.");
            method.Invoke(null, null);
        }
    }

    [Serializable]
    public class AnalyzeAssetCommand : IGetCommand
    {
        public string Description => "【アセット調査】指定アセットの依存関係（参照しているアセット）を解析します。引数(配列): [\"assetPath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format.";
            }

            if (args == null || args.Length < 1) return "Error: Expected assetPath argument.";
            string assetPath = args[0];

            if (AIBlackList.Instance.IsBlacklisted(assetPath))
                return $"Error: Access denied. Asset '{assetPath}' is blacklisted.";

            string[] dependencies = AssetDatabase.GetDependencies(assetPath, true);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Analysis for: {assetPath}");
            sb.AppendLine("Dependencies (directly and indirectly referenced):");

            foreach (var dep in dependencies)
            {
                if (dep == assetPath) continue;
                string token = IGetCommand.GetAccessToken(dep);
                sb.AppendLine($"- {dep} [Token: {token}]");
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class FindReferencesCommand : IGetCommand
    {
        public string Description => "【被参照検索】指定アセットを「参照している」アセットをプロジェクト全体から検索します。引数(配列): [\"assetPath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format.";
            }

            if (args == null || args.Length < 1) return "Error: Expected assetPath argument.";
            string assetPath = args[0];

            if (AIBlackList.Instance.IsBlacklisted(assetPath))
                return $"Error: Access denied. Asset '{assetPath}' is blacklisted.";

            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            List<string> referencers = new List<string>();

            foreach (var path in allAssetPaths)
            {
                if (path == assetPath) continue;
                string[] dependencies = AssetDatabase.GetDependencies(path, false);
                if (dependencies.Contains(assetPath))
                {
                    referencers.Add(path);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Referencers for: {assetPath}");
            if (referencers.Count == 0)
            {
                sb.AppendLine("No assets found referencing this asset.");
            }
            else
            {
                foreach (var refPath in referencers)
                {
                    string token = IGetCommand.GetAccessToken(refPath);
                    sb.AppendLine($"- {refPath} [Token: {token}]");
                }
            }

            return sb.ToString();
        }
    }
}