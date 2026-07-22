using System;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// ステートコンテナの全機能を保持するインターフェース
    /// </summary>
    public interface IStateContainer : IStateContainerGetter, IStateContainerSetter, IStateContainerRegistration
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
    /// StateContainerにActionの登録をする際に利用する
    /// </summary>
    public interface IStateContainerSetter
    {
        /// <summary>
        /// 特定のStateにイベントを登録する。
        /// 戻り値のsubscriptionをDisposeすることで、登録者自身が解除できる。
        /// </summary>
        /// <param name="action">登録するアクション</param>
        /// <param name="phase">どのタイミングで実行するか</param>
        /// <param name="subscription">購読解除用のIDisposable</param>
        /// <typeparam name="T">イベント登録を行う対象のState</typeparam>
        /// <returns>登録できた場合はtrueを返す</returns>
        public bool TryRegisterEvent<T>(Action<IStateContainerGetter> action, StatePhase phase, out IDisposable subscription)
            where T : IStateGetter;
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
        /// <typeparam name="TGetter">IStateGetterを継承したStateの取得インターフェース</typeparam>
        /// <typeparam name="TState">登録するStateの型</typeparam>
        /// <returns>登録できた場合はtrueを返す(同一Getter型が登録済みの場合はfalse)</returns>
        public bool TryRegisterState<TGetter, TState>(TState state)
            where TGetter : IStateGetter
            where TState : StateBase, TGetter;
    }
}