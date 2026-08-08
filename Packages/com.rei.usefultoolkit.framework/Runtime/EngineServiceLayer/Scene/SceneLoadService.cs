using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// SceneManagerを直接扱う唯一のEngineServiceLayerクラス。
    /// ISceneChangeEvent経由でロードメソッドを登録し、Application側からのリクエストに応じて
    /// 現在ロード中のシーン(このサービス自身が読み込んだものに限る)と目標シーン集合を差分比較し、
    /// 不要な分だけUnload、不足分だけAdditiveでLoadする。LoadSceneMode.Singleは使わない
    /// (System/Bootシーンなど、このサービスが管理していないシーンを巻き込んで消してしまうため)。
    /// </summary>
    public sealed class SceneLoadService<T> : IDisposable where T : Enum
    {
        private readonly HashSet<string> _loadedSceneNames = new();
        private IDisposable _registration;

        public void SetSceneChangeEvent(ISceneChangeEvent<T> changeEvent)
        {
            _registration = changeEvent.RegisterSceneLoader(LoadScenesAsync);
        }

        public void Dispose()
        {
            _registration?.Dispose();
            _registration = null;
        }

        private async UniTask LoadScenesAsync(T[] scenesToLoad, IProgress<float> progress)
        {
            var targetNames = scenesToLoad.Select(scene => scene.ToString()).ToHashSet();

            var toUnload = _loadedSceneNames.Where(name => !targetNames.Contains(name)).ToArray();
            var toLoad = targetNames.Where(name => !_loadedSceneNames.Contains(name)).ToArray();

            var totalSteps = toUnload.Length + toLoad.Length;
            var completedSteps = 0;

            foreach (var sceneName in toUnload)
            {
                var operation = SceneManager.UnloadSceneAsync(sceneName);
                if (operation != null) await operation.ToUniTask();

                _loadedSceneNames.Remove(sceneName);
                completedSteps++;
                progress?.Report(totalSteps == 0 ? 1f : (float)completedSteps / totalSteps);
            }

            foreach (var sceneName in toLoad)
            {
                var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                await operation.ToUniTask();

                _loadedSceneNames.Add(sceneName);
                completedSteps++;
                progress?.Report(totalSteps == 0 ? 1f : (float)completedSteps / totalSteps);
            }
        }
    }
}
