using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
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
        /// <summary> ステートの状態を取得するためのメソッド </summary>
        public abstract string GetLog();
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
    
    public class TestState : GameStateBase, IStateGetter
    {
        public override string GetLog()
        {
            throw new NotImplementedException();
        }
    }
}