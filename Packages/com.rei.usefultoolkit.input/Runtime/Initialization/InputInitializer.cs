using UnityEngine;
using UsefulToolkit.Application.Input;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.EngineService.Input;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 入力システムを初期化する Initializer。常駐シーンへ置く想定。
    ///
    /// InputState の生成と BlackBoard への登録は <see cref="IInputManager"/> の実装が行い、
    /// この Initializer は <see cref="InputDispatcher"/> の初期化だけを担当する。
    /// InputDispatcher は BlackBoard から InputState を取得するため、
    /// Application の初期化を InputDispatcher より先に行う順序が前提になる。
    ///
    /// (map, action) 単位の橋渡し(<see cref="IInputDispatcher.Bind{TValue}"/>)は利用者側の enum に
    /// 依存するため、ここでは行わない。BlackBoard から IInputState を取得したシーン側の
    /// Initializer から、その Dispatcher 越しに呼ぶこと。
    /// </summary>
    public sealed class InputInitializer : InitializerBase
    {
        [SerializeField] private InputDispatcher _inputDispatcher;

        [SerializeReference]
        [SubclassSelector]
        [Tooltip("InputState を生成して BlackBoard へ登録する Application クラス。")]
        private IInputManager _inputManager = new InputManager();

        /// <summary>
        /// Application の初期化で InputState を用意させたうえで、InputDispatcher を初期化する。
        /// </summary>
        /// <param name="blackBoard">InputState の登録先</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            if (_inputDispatcher == null)
            {
                UsefulLogger.LogError("InputDispatcher が設定されていない為、入力システムを初期化できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            if (_inputManager == null)
            {
                UsefulLogger.LogError("IInputManager が設定されていない為、InputState を生成できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            // --  ここにApplicationの初期化を配置。内部でInputStateを生成してBlackBoardに登録 --
            _inputManager.Initialize(blackBoard, _inputDispatcher);

            _inputDispatcher.SetBlackBoard(blackBoard);
            _inputDispatcher.Initialize();

            base.Initialize(blackBoard);
        }
    }
}
