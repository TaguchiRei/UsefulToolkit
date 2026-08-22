using System;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// シーンStateのうち、EngineServiceLayerへ向けた面。
    /// 読み取り面に、実際のシーン読み込み処理を預ける口を1つ足したもの。
    /// </summary>
    public interface ISceneLoaderRegister : ISceneStateGetter
    {
        /// <summary>
        /// シーン読み込み処理を登録する。以降のシーン遷移はここで登録した処理が実行する。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        /// <exception cref="ArgumentNullException">loaderがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">すでにロード処理が登録されているときに出力</exception>
        IDisposable RegisterSceneLoader(SceneLoadRequest loader);
    }
}
