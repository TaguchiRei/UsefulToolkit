using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.Architecture;

namespace UsefulToolkit.EngineService
{
    public class SceneLoader : InitializableMonoBehaviour
    {
        public AsyncOperation LoadScene(string sceneName)
        {
            return SceneManager.LoadSceneAsync(sceneName);
        }

        public AsyncOperation LoadScene(int sceneBuildIndex)
        {
            return SceneManager.LoadSceneAsync(sceneBuildIndex);
        }
    }
}