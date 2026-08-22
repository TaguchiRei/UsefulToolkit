using UnityEngine;
using System.IO;

namespace UsefulToolkit.Editor.GitSupport
{
    public class BranchService
    {
        public static string GetBranchName()
        {
            var head = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "../.git/HEAD"));
            return head.Replace("ref: refs/heads/", "").Trim();
        }
    }
}