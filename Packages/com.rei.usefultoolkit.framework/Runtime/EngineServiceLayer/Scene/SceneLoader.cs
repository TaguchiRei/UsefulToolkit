using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsefulToolkit.Architecture;

namespace UsefulToolkit.EngineService
{
    public class SceneLoader : InitializableMonoBehaviour, ISceneLoader
    {
        #region LoadMultiScene

        

        #endregion
        #region LoadScene

        private AsyncOperation LoadScene(string sceneName)
        {
            return SceneManager.LoadSceneAsync(sceneName);
        }

        private AsyncOperation LoadScene(int sceneBuildIndex)
        {
            return SceneManager.LoadSceneAsync(sceneBuildIndex);
        }

        #endregion

        #region UnloadScene

        private AsyncOperation UnloadScene(string sceneName)
        {
            return SceneManager.UnloadSceneAsync(sceneName);
        }

        private AsyncOperation UnloadScene(int sceneBuildIndex)
        {
            return SceneManager.UnloadSceneAsync(sceneBuildIndex);
        }

        #endregion

        public IDisposable RegisterLoadSceneProgress(Action<float> progress)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterUnLoadSceneProgress(Action<float> progress)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterStartLoadScene(Action action)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterEndLoadScene(Action action)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterStartUnLoadScene(Action action)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterEndUnLoadScene(Action action)
        {
            throw new NotImplementedException();
        }
    }
}