using System;

namespace UsefulToolkit.EngineService
{
    /// <summary>
    /// シーンロードの開始、進捗等により実行されるイベントを登録するためのインターフェース
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>
        /// シーンロード進捗を引数にとるイベントをシーン読み込みに登録する
        /// </summary>
        /// <param name="progress">シーンロード進捗を0~1の値で引数にとるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterLoadSceneProgress(Action<float> progress);

        /// <summary>
        /// シーンアンロード進捗を引数にとるイベントをシーン読み込みに登録する
        /// </summary>
        /// <param name="progress">シーンアンロード進捗を0~1の値で引数にとるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterUnLoadSceneProgress(Action<float> progress);

        /// <summary>
        /// ロード開始を通知するイベントを登録できる
        /// </summary>
        /// <param name="action">実行されるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterStartLoadScene(Action action);

        /// <summary>
        /// ロード終了を通知するイベントを登録できる
        /// </summary>
        /// <param name="action">実行されるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterEndLoadScene(Action action);

        /// <summary>
        /// アンロード開始を通知するイベントを登録できる
        /// </summary>
        /// <param name="action">実行されるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterStartUnLoadScene(Action action);

        /// <summary>
        /// アンロード終了を通知するイベントを登録できる
        /// </summary>
        /// <param name="action">実行されるアクション</param>
        /// <returns>Disposeで登録を解除</returns>
        IDisposable RegisterEndUnLoadScene(Action action);
    }
}