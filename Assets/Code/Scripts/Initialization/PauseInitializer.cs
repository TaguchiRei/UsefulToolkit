using Sandbox.Application;
using Sandbox.BlackBoard;
using UnityEngine;
using UsefulToolkit.Initialization;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Sandbox.Initialization
{
    /// <summary>
    /// PauseManagerを生成し、他のInitializerが使えるようIPauseManagerとして登録する側。
    /// 登録はCollectionフェーズ(Awake)でなければ受け付けられない。
    /// </summary>
    public sealed class PauseInitializer : InitializerBase
    {
        private PauseManager _pauseManager;

        private void Awake()
        {
            _pauseManager = new PauseManager();

            // 実装クラスではなくインターフェースをキーにして登録する。
            // 受け取る側はIPauseManagerしか知らずに済む。
            bool registered = GameCompositer.TryRegisterContent<IPauseManager>(_pauseManager);
            Debug.Log($"[CompositerTest] PauseInitializer.Awake IPauseManager登録 : {registered}");
        }

        public override void Initialize(IBlackBoard blackBoard)
        {
            base.Initialize(blackBoard);

            // 実際のプロジェクトではここでStateを生成しBlackBoardへ登録する
            bool foundStateBoard = blackBoard.TryGetStateBoard<CompositerTestStateBoard>(out _);
            Debug.Log($"[CompositerTest] PauseInitializer.Initialize stateBoard={foundStateBoard}");
        }
    }
}
