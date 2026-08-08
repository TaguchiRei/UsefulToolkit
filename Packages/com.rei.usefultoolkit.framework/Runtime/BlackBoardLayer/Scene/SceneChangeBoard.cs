using System;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン読み込みのトリガーを担うChildEventBoard。
    /// EngineServiceLayer側(SceneLoadServiceBase)がRegisterSceneLoaderでロードメソッドを渡し、
    /// Application側(SceneFlowController)がRequestTransitionAsyncでそれを呼び出す。
    /// IEventChannel(Register専用)/EventChannel(Publish可能)と同じ考え方で、
    /// 登録側と起動側のAPIを分けている。
    /// </summary>
    public sealed class SceneChangeBoard<T> : ChildEventBoardBase, ISceneChangeEvent<T> where T : Enum
    {
        private Func<T[], IProgress<float>, UniTask> _loader;

        public IDisposable RegisterSceneLoader(Func<T[], IProgress<float>, UniTask> loader)
        {
            if (_loader != null)
                throw new InvalidOperationException("SceneLoaderは既に登録されています。二重登録はできません。");

            _loader = loader;
            return new StateDispose(() => _loader = null);
        }

        /// <summary>Application側: 実際のシーン読み込みをリクエストする。</summary>
        public UniTask RequestTransitionAsync(T[] scenesToLoad, IProgress<float> progress)
        {
            if (_loader == null)
                throw new InvalidOperationException("SceneLoaderが登録されていません。");

            return _loader(scenesToLoad, progress);
        }
    }
}
