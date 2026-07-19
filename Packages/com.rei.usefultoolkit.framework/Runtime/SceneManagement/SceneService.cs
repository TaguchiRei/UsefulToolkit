using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsefulToolkit.Framework
{
    public class SceneService
    {
        public static SceneService Instance => _instance ??= new SceneService();

        private static SceneService _instance;

        private SceneService()
        {
            SceneManager.GetActiveScene();
        }
    }
}