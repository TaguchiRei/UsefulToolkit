using System;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン読み込みのトリガー経路をEventBoard形式で公開するインターフェース。
    /// SceneLoadServiceBase(EngineServiceLayer)が自身のロードメソッドをRegisterし、
    /// Application側はSceneChangeBoard.RequestTransitionAsyncを通じてそれを起動する。
    /// </summary>
    public interface ISceneChangeEvent<T> where T : Enum
    {
        /// <summary>
        /// シーン読み込みメソッドを登録する。二重登録は例外を投げる。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        IDisposable RegisterSceneLoader(Func<T[], IProgress<float>, UniTask> loader);
    }
}
