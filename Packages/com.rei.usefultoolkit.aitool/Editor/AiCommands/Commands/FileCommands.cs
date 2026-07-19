using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    public static class PathValidator
    {
        public static string ResolvePath(string path)
        {
            string assetsPath = Path.GetFullPath(Application.dataPath);
            string absolutePath;

            if (string.IsNullOrEmpty(path) || path.Equals("Assets", StringComparison.OrdinalIgnoreCase) || path.Equals("Assets/", StringComparison.OrdinalIgnoreCase) || path.Equals("Assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return assetsPath;
            }

            if (Path.IsPathRooted(path))
            {
                absolutePath = Path.GetFullPath(path);
            }
            else
            {
                // "Assets/" または "Assets\" で始まる場合は、それを除去して dataPath と結合する
                string relative = path;
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) relative = path.Substring(7);
                else if (path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase)) relative = path.Substring(7);
                else if (path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase) && path.Length == 6) relative = "";

                absolutePath = Path.GetFullPath(Path.Combine(assetsPath, relative));
            }
            return absolutePath;
        }

        public static bool IsPathInsideAssets(string path)
        {
            try
            {
                string assetsPath = Path.GetFullPath(Application.dataPath);
                string absolutePath = ResolvePath(path);
                
                string normalizedPath = absolutePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
                string normalizedAssets = assetsPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

                return normalizedPath.StartsWith(normalizedAssets, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string GetAssetPath(string absolutePath)
        {
            string assetsPath = Path.GetFullPath(Application.dataPath);
            string normalizedPath = absolutePath.Replace('\\', '/');
            string normalizedAssets = assetsPath.Replace('\\', '/');

            if (normalizedPath.StartsWith(normalizedAssets, StringComparison.OrdinalIgnoreCase))
            {
                string relative = normalizedPath.Substring(normalizedAssets.Length).TrimStart('/');
                return string.IsNullOrEmpty(relative) ? "Assets" : "Assets/" + relative;
            }
            return null;
        }

        public static bool IsBlacklisted(string path)
        {
            string absPath = ResolvePath(path);
            string assetPath = GetAssetPath(absPath);
            return AIBlackList.Instance.IsBlacklisted(assetPath);
        }
    }

    [Serializable]
    public class ReadFileCommand : IGetCommand
    {
        public string Description => "指定したパスのファイル内容を読み取ります。引数(配列): [\"filePath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"filePath\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected filePath argument.";
            
            if (PathValidator.IsBlacklisted(args[0]))
                return $"Error: Access denied. Path '{args[0]}' is blacklisted.";

            string filePath = PathValidator.ResolvePath(args[0]);

            if (!PathValidator.IsPathInsideAssets(filePath))
                return $"Error: Access denied. Path '{filePath}' must be inside Assets folder.";
            if (!File.Exists(filePath)) return $"Error: File '{filePath}' not found.";

            try
            {
                string content = File.ReadAllText(filePath);
                string token = IGetCommand.GetAccessToken(filePath);
                return $"Token: {token}\nContent:\n{content}";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }

    [Serializable]
    public class GetFilePathCommand : IGetCommand
    {
        public string Description => "アセット名からファイルパスを検索します。引数(配列): [\"fileName\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"fileName\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected fileName argument.";
            string fileName = args[0];

            string[] guids = AssetDatabase.FindAssets(fileName);
            if (guids.Length == 0) return $"Error: No assets found for '{fileName}'.";

            StringBuilder sb = new StringBuilder();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AIBlackList.Instance.IsBlacklisted(path)) continue;
                sb.AppendLine(path);
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class GetFolderPathCommand : IGetCommand
    {
        public string Description => "フォルダ名からフォルダパスを検索します。引数(配列): [\"folderName\"]。※プロジェクトルートのAssetsフォルダを検索したい場合は \"Assets\" を指定してください。";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"folderName\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected folderName argument.";
            string folderName = args[0];

            string resolvedPath = PathValidator.ResolvePath(folderName);
            if (Directory.Exists(resolvedPath))
            {
                string assetPath = PathValidator.GetAssetPath(resolvedPath);
                if (AIBlackList.Instance.IsBlacklisted(assetPath))
                    return "Error: Access denied. Path is blacklisted.";
                return assetPath ?? resolvedPath;
            }

            string[] guids = AssetDatabase.FindAssets(folderName + " t:Folder");
            if (guids.Length == 0) return $"Error: No folders found for '{folderName}'.";

            StringBuilder sb = new StringBuilder();
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AIBlackList.Instance.IsBlacklisted(path)) continue;
                sb.AppendLine(path);
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class ReadFolderCommand : IGetCommand
    {
        public string Description => "指定したパスのフォルダ内のエントリ一覧を取得します。引数(配列): [\"folderPath\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"folderPath\"].";
            }

            if (args == null || args.Length < 1) return "Error: Expected folderPath argument.";
            
            if (PathValidator.IsBlacklisted(args[0]))
                return $"Error: Access denied. Path '{args[0]}' is blacklisted.";

            string folderPath = PathValidator.ResolvePath(args[0]);

            if (!PathValidator.IsPathInsideAssets(folderPath))
                return $"Error: Access denied. Path '{folderPath}' must be inside Assets folder.";
            if (!Directory.Exists(folderPath)) return $"Error: Directory '{folderPath}' not found.";

            string token = IGetCommand.GetAccessToken(folderPath);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Folder: {folderPath}");
            sb.AppendLine($"AccessToken: {token}");
            sb.AppendLine("Contents:");

            foreach (var entry in Directory.GetFileSystemEntries(folderPath))
            {
                string assetPath = PathValidator.GetAssetPath(entry);
                if (AIBlackList.Instance.IsBlacklisted(assetPath)) continue;

                string type = Directory.Exists(entry) ? "[Folder]" : "[File]";
                sb.AppendLine($"{type} {Path.GetFileName(entry)}");
            }

            return sb.ToString();
        }
    }

    [Serializable]
    public class CreateFileCommand : ISetCommand
    {
        public string Description =>
            "【ファイル作成】指定した親フォルダのアクセストークンを用いて、新しいファイルを作成します。引数(配列): [\"parentFolderToken\", \"fileName\", \"content\"]。※先にReadFolderCommand等で対象フォルダのアクセストークンを取得してください。";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"parentFolderToken\", \"fileName\", \"content\"].";
            }

            if (args == null || args.Length != 3)
                return
                    $"Error: Invalid argument count. Expected 3 (parentFolderToken, fileName, content), got {args?.Length ?? 0}.";

            string parentToken = args[0];
            string fileName = args[1];
            string content = args[2];

            if (!ISetCommand.GetFromAccessToken(parentToken, out string parentPath))
            {
                return "Error: Invalid ParentFolderToken.";
            }

            if (AIBlackList.Instance.IsBlacklisted(parentPath))
                return $"Error: Access denied. Parent folder '{parentPath}' is blacklisted.";

            if (!PathValidator.IsPathInsideAssets(parentPath))
                return "Error: Access denied. Parent folder must be inside Assets folder.";
            if (!Directory.Exists(parentPath)) return $"Error: Parent directory '{parentPath}' not found.";

            string fullPath = Path.Combine(parentPath, fileName);
            if (AIBlackList.Instance.IsBlacklisted(fullPath))
                return $"Error: Access denied. File path '{fullPath}' is blacklisted.";

            if (!PathValidator.IsPathInsideAssets(fullPath))
            {
                return "Error: Access denied. File must be inside Assets folder.";
            }

            try
            {
                FileGenerator.WriteFile(fullPath, content);
                string newToken = IGetCommand.GetAccessToken(fullPath);
                return $"Success: Created file at {fullPath}. New AccessToken: {newToken}";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }

    [Serializable]
    public class CreateFolderCommand : ISetCommand
    {
        public string Description => "【フォルダ作成】指定した親フォルダのアクセストークンを用いて、新しいフォルダを作成します。引数(配列): [\"parentFolderToken\", \"folderName\"]。※先にReadFolderCommand等で対象フォルダのアクセストークンを取得してください。";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"parentFolderToken\", \"folderName\"].";
            }

            if (args == null || args.Length < 2) return "Error: Expected [parentFolderToken, folderName].";

            string parentToken = args[0];
            string folderName = args[1];

            if (!ISetCommand.GetFromAccessToken(parentToken, out var parentPath))
            {
                return "Error: Invalid ParentFolderToken.";
            }

            if (AIBlackList.Instance.IsBlacklisted(parentPath))
                return $"Error: Access denied. Parent folder '{parentPath}' is blacklisted.";

            if (!PathValidator.IsPathInsideAssets(parentPath))
                return "Error: Access denied. Parent folder must be inside Assets folder.";

            string fullPath = Path.Combine(parentPath, folderName);
            if (AIBlackList.Instance.IsBlacklisted(fullPath))
                return $"Error: Access denied. Folder path '{fullPath}' is blacklisted.";

            if (!PathValidator.IsPathInsideAssets(fullPath))
            {
                return "Error: Access denied. Folder must be inside Assets folder.";
            }

            try
            {
                Directory.CreateDirectory(fullPath);
                AssetDatabase.Refresh();
                string newToken = IGetCommand.GetAccessToken(fullPath);
                return $"Success: Created folder at {fullPath}. New AccessToken: {newToken}";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }

    [Serializable]
    public class ChangeFileCommand : ISetCommand
    {
        public string Description => "指定したファイルのアクセストークンを用いて、内容を書き換えます。引数(配列): [\"accessToken\", \"content\"]";

        public string Execute(string argument)
        {
            string[] args;
            try
            {
                args = JsonConvert.DeserializeObject<string[]>(argument);
            }
            catch
            {
                return "Error: Invalid argument format. Expected [\"accessToken\", \"content\"].";
            }

            if (args == null || args.Length < 2) return "Error: Expected [accessToken, content].";

            string token = args[0];
            string content = args[1];

            if (!ISetCommand.GetFromAccessToken(token, out string path))
            {
                return "Error: Invalid AccessToken.";
            }

            if (AIBlackList.Instance.IsBlacklisted(path))
                return $"Error: Access denied. File '{path}' is blacklisted.";

            if (!PathValidator.IsPathInsideAssets(path))
                return "Error: Access denied. Path must be inside Assets folder.";
            if (!File.Exists(path)) return $"Error: File '{path}' not found.";

            try
            {
                FileGenerator.WriteFile(path, content);
                return $"Success: Updated file {path}.";
            }
            catch (Exception e)
            {
                return $"Error: {e.Message}";
            }
        }
    }
}
