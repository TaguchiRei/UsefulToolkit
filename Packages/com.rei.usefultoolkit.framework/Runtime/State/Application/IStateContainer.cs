using System;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// ステートコンテナの全機能を保持するインターフェース
    /// </summary>
    public interface IStateContainer : IStateContainerGetter, IStateContainerRegistration
    {
    }

    /// <summary>
    /// StateContainerから状態を取得する際に利用する
    /// </summary>
    public interface IStateContainerGetter
    {
        /// <summary>
        /// StateのGetterのみのインターフェースを取得する
        /// </summary>
        /// <param name="state">取得したGetter</param>
        /// <typeparam name="T">取得するStateGetterの型</typeparam>
        /// <returns>取得できた場合はtrueを返す</returns>
        public bool TryGetStateGetter<T>(out T state) where T : IStateGetter;
    }

    /// <summary>
    /// StateContainerにStateの登録、解除を行う
    /// </summary>
    public interface IStateContainerRegistration
    {
        /// <summary>
        /// ゲームステートを登録する
        /// </summary>
        /// <param name="state">登録するステートの実体</param>
        /// <typeparam name="T">登録するステートのクラス</typeparam>
        /// <returns>登録できたらtrueを返す</returns>
        public bool TryRegisterGameState<T>(T state)
            where T : GameStateBase, IStateGetter;

        /// <summary>
        /// シーンステートを登録する
        /// </summary>
        /// <param name="state">登録するステートの実体</param>
        /// <param name="scene">シーン名Enumを利用</param>
        /// <typeparam name="T">登録するステートのクラス</typeparam>
        /// <typeparam name="TSceneEnum">ステートが依存するシーン</typeparam>
        /// <returns>登録できたらtrueを返す</returns>
        public bool TryRegisterSceneState<T, TSceneEnum>(T state, TSceneEnum scene)
            where T : SceneStateBase, IStateGetter
            where TSceneEnum : Enum;

        /// <summary>
        /// 登録解除が可能なステートを登録する
        /// </summary>
        /// <param name="state">登録するステートの実体</param>
        /// <typeparam name="T">登録するステートのクラス</typeparam>
        /// <returns>登録できたらtrueを返す</returns>
        public bool TryRegisterUnRegistableState<T>(T state)
            where T : UnRegistableStateBase, IStateGetter;

        /// <summary>
        /// ステートの登録を解除する。 
        /// </summary>
        /// <typeparam name="T">UnRegistableStateBaseを継承したステートのクラス</typeparam>
        /// <returns></returns>
        public bool TryUnRegisterState<T>()
            where T : UnRegistableStateBase, IStateGetter;
    }
}