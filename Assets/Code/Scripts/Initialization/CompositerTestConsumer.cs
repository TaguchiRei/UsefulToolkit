using Sandbox.BlackBoard;
using UnityEngine;
using UsefulToolkit.Architecture;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Sandbox.Initialization
{
    /// <summary>依存を受け取る側。BlackBoardとProviderの両方をInjectで受け取る。</summary>
    public sealed class CompositerTestConsumer : InitializerBase,
        IInjectable<IBlackBoard, CompositerTestProvider>
    {
        private IBlackBoard _blackBoard;
        private CompositerTestProvider _provider;

        public void Inject(IBlackBoard blackBoard, CompositerTestProvider provider)
        {
            _blackBoard = blackBoard;
            _provider = provider;
            Debug.Log($"[CompositerTest] Consumer.Inject blackBoard={blackBoard != null} provider={provider != null}");
        }

        public override void Initialize()
        {
            base.Initialize();

            bool foundStateBoard = _blackBoard.TryGetStateBoard<CompositerTestStateBoard>(out _);
            bool foundEventBoard = _blackBoard.TryGetEventBoard<CompositerTestEventBoard>(out _);

            Debug.Log($"[CompositerTest] Consumer.Initialize stateBoard={foundStateBoard} eventBoard={foundEventBoard}");
        }
    }
}
