using UnityEngine;
using System.IO;

namespace UsefulToolkit.GitSupport
{
    public class BranchService
    {
        public static string GetBranchName()
        {
            var head = File.ReadAllText(Path.Combine(Application.dataPath, "../.git/HEAD"));
            return head.Replace("ref: refs/heads/", "").Trim();
        }
    }
}