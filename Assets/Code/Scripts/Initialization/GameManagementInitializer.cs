using Sandbox.Application;
using Sandbox.BlackBoard;
using UnityEngine;
using UsefulToolkit.Architecture;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Sandbox.Initialization
{
    /// <summary>
    /// GameManagerを初期化する側。GameManagerが必要とするIPauseManagerを自分で探さず、
    /// IInjectableとして宣言してCompositerから渡してもらう。
    /// この宣言がそのままCompositer生成時のヒントになる。
    /// </summary>
    public sealed class GameManagementInitializer : InitializerBase, IInjectable<IPauseManager>
    {
        private IPauseManager _pauseManager;
        private GameManager _gameManager;

        public void Inject(IPauseManager pauseManager)
        {
            _pauseManager = pauseManager;
            Debug.Log($"[CompositerTest] GameManagementInitializer.Inject pauseManager={pauseManager != null}");
        }

        public override void Initialize(IBlackBoard blackBoard)
        {
            base.Initialize(blackBoard);

            // Injectで揃った依存を初期化対象へ渡す。ここがInitializerの本来の仕事。
            _gameManager = new GameManager(_pauseManager);
            _gameManager.Boot();

            bool foundEventBoard = blackBoard.TryGetEventBoard<CompositerTestEventBoard>(out _);
            Debug.Log($"[CompositerTest] GameManagementInitializer.Initialize eventBoard={foundEventBoard}");
        }
    }
}
