using System;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class GetSceneStructureCommand : IGetCommand
    {
        public string Description => "【シーン構造取得】現在のシーン全体の構造（GameObjectの階層）を名前で取得します。引数(配列): []。※詳細情報は各GameObject名を用いてGetSceneObjectCommandで取得してください。";
        public string Execute(string argument)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                if (AIBlackList.Instance.IsBlacklisted(scene.path))
                {
                    sb.AppendLine($"Scene: {scene.name} [ACCESS DENIED]");
                    continue;
                }

                sb.AppendLine($"Scene: {scene.name}");
                GameObject[] roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    Traverse(root, sb, 1, "");
                }
            }
            return sb.ToString();
        }

        private void Traverse(GameObject obj, StringBuilder sb, int indent, string parentPath)
        {
            string currentPath = parentPath + "/" + obj.name;
            if (AIBlackList.Instance.IsBlacklisted(currentPath)) return;

            sb.Append(new string(' ', indent * 2));
            sb.AppendLine(obj.name);

            foreach (Transform child in obj.transform)
            {
                Traverse(child.gameObject, sb, indent + 1, currentPath);
            }
        }
    }

    [Serializable]
    public class GetChildrenCommand : IGetCommand
    {
        public string Description => "【子要素取得】指定したGameObjectの直接の子要素一覧を名前で取得します。引数(配列): [\"gameObjectPath\"]。";
        public string Execute(string argument)
        {
            string[] args;
            try { args = JsonConvert.DeserializeObject<string[]>(argument); }
            catch { return "Error: Invalid argument format. Expected [\"gameObjectPath\"]."; }

            if (args == null || args.Length < 1) return "Error: Expected gameObjectPath argument.";
            string pathArg = args[0];

            if (AIBlackList.Instance.IsBlacklisted(pathArg))
                return $"Error: Access denied. GameObject '{pathArg}' is blacklisted.";

            GameObject target = GameObject.Find(pathArg);
            if (target == null) return $"Error: GameObject '{pathArg}' not found.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Children of {target.name}:");
            foreach (Transform child in target.transform)
            {
                string childPath = pathArg.TrimEnd('/') + "/" + child.name;
                if (AIBlackList.Instance.IsBlacklisted(childPath)) continue;
                
                // 設計思想：リストアップの際にトークンを自動生成して渡さない
                sb.AppendLine($"- {child.name}");
            }
            return sb.ToString();
        }
    }

    [Serializable]
    public class GetSceneObjectCommand : IGetCommand
    {
        public string Description => "【詳細情報取得】指定したGameObjectの詳細情報とアクセストークンを取得します。引数(配列): [\"gameObjectPath\"]。※このトークンを使用してChangeInspectorCommand等で編集可能です。";
        public string Execute(string argument)
        {
            string[] args;
            try { args = JsonConvert.DeserializeObject<string[]>(argument); }
            catch { return "Error: Invalid argument format. Expected [\"gameObjectPath\"]."; }

            if (args == null || args.Length < 1) return "Error: Expected gameObjectPath argument.";
            string pathArg = args[0];

            if (AIBlackList.Instance.IsBlacklisted(pathArg))
                return $"Error: Access denied. GameObject '{pathArg}' is blacklisted.";

            GameObject target = GameObject.Find(pathArg);
            if (target == null) return $"Error: GameObject '{pathArg}' not found.";

            string path = GetGameObjectPath(target);
            string token = IGetCommand.GetAccessToken(path);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Name: {target.name}");
            sb.AppendLine($"Path: {path}");
            sb.AppendLine($"Active: {target.activeSelf}");
            sb.AppendLine($"Tag: {target.tag}");
            sb.AppendLine($"Layer: {LayerMask.LayerToName(target.layer)}");
            sb.AppendLine($"AccessToken: {token}");
            
            sb.AppendLine("Components:");
            foreach (var component in target.GetComponents<Component>())
            {
                if (component == null) continue;
                sb.AppendLine($"- {component.GetType().Name}");
            }

            return sb.ToString();
        }

        private string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }
    }
}
