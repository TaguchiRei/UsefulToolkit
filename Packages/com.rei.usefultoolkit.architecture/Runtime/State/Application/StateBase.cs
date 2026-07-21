using System;
using System.Collections.Generic;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// <code>
    /// ステートのベース型。
    /// これを継承させたクラスにIStateGetterインターフェースを継承したGetterインターフェースを実装する。
    ///
    /// 使用例:
    /// public interface IPlayerStateGetter : IStateGetter
    /// {
    ///     int PlayerHp { get; }
    /// }
    ///
    /// public class PlayerState : StateBase, IPlayerStateGetter
    /// {
    ///     public int PlayerHp => _playerHp;
    ///     private int _playerHp;
    /// }
    /// </code>
    /// </summary>
    public abstract class StateBase : IDisposable
    {
        private readonly List<Action<IStateContainerGetter>> _onStartActions = new();
        private readonly List<Action<IStateContainerGetter>> _onStayActions = new();
        private readonly List<Action<IStateContainerGetter>> _onExitActions = new();

        private bool _disposed;

        /// <summary>
        /// ステートにイベントを登録する。
        /// 戻り値のIDisposableをDisposeすることで、登録した購読者自身が解除できる。
        /// </summary>
        /// <param name="action">登録するAction</param>
        /// <param name="phase">実行するフェーズ</param>
        /// <returns>購読解除用のIDisposable</returns>
        public IDisposable RegisterEventToState(Action<IStateContainerGetter> action, StatePhase phase)
        {
            var list = GetActionList(phase);
            list.Add(action);
            return new DisposableAction(() => list.Remove(action));
        }

        /// <summary>
        /// 指定フェーズに登録されたイベントを発火する。
        /// 呼び出しはキューイングされた上での発火を想定し、State変更の即時再帰呼び出しを避けること。
        /// </summary>
        /// <param name="phase">発火するフェーズ</param>
        /// <param name="getter">購読者に渡す読み取り専用インターフェース</param>
        protected void NotifyEvent(StatePhase phase, IStateContainerGetter getter)
        {
            var list = GetActionList(phase);
            var snapshot = list.ToArray();
            foreach (var action in snapshot)
            {
                action.Invoke(getter);
            }
        }

        private List<Action<IStateContainerGetter>> GetActionList(StatePhase phase)
        {
            return phase switch
            {
                StatePhase.Start => _onStartActions,
                StatePhase.Stay => _onStayActions,
                StatePhase.End => _onExitActions,
                _ => throw new ArgumentOutOfRangeException(nameof(phase))
            };
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _onStartActions.Clear();
            _onStayActions.Clear();
            _onExitActions.Clear();
        }
    }

    /// <summary>
    /// StateのGetterインターフェースを作るための基盤インターフェース
    /// <code>
    /// 使用例
    /// public interface IPlayerStateGetter : IStateGetter
    /// {
    ///     int PlayerHp { get; }
    /// }
    /// </code>
    /// </summary>
    public interface IStateGetter
    {
    }
}