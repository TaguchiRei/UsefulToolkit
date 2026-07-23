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
        /// Stateを登録する
        /// </summary>
        /// <param name="state">登録するState</param>
        /// <returns>登録できた場合はtrueを返す(同一Getter型が登録済みの場合はfalse)</returns>
        public bool TryRegisterState<T>(T state)
            where T : StateBase, IStateGetter;

        public bool TryRegisterGameState<T>(T state)
            where T : GameStateBase, IStateGetter;

        public bool TryRegisterSceneState<T, TSceneEnum>(T state, TSceneEnum scene)
            where T : SceneStateBase, IStateGetter
            where TSceneEnum : Enum;

        public bool TryRegisterUnRegistableState<T>(T state)
            where T : UnRegistableStateBase, IStateGetter;
    }
}