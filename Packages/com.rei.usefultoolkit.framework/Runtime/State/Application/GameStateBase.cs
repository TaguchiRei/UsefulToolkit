using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// ゲーム終了時まで残るステートのベースクラス
    /// </summary>
    public class GameStateBase : StateBase
    {
        public sealed override StateLifeScope LifeScope => StateLifeScope.OnGameEnd;
    }
}