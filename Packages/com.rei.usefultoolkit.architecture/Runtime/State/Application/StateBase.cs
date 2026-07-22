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
    public abstract class StateBase 
    {
        
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
        bool TryRegisterStateEvent<T>(Action<StateContext<T>> action);
    }
}