using UnityEngine;
using UsefulToolkit.Application.Input;
using UsefulToolkit.Attributes;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.EngineService.Input;
using UsefulToolkit.Utility;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 入力システムを初期化する Initializer の基底。常駐シーンへ置く想定。
    ///
    /// InputState の生成と BlackBoard への登録は <see cref="IInputManager"/> の実装が行い、
    /// このクラスは <see cref="InputDispatcher"/> の初期化と、その両者の結線だけを担当する。
    ///
    /// 操作面(<see cref="IInputController"/>)を DI コンテナへ登録するには生成された Compositor の
    /// 具象型が要り、それはこのパッケージからは参照できない。そのため利用者のアセンブリ側へ
    /// 派生クラスを生成し、そこから <see cref="Controller"/> を登録する。生成は
    /// <c>UsefulToolkit/Scene/GenerateUsefulPersistentScene</c> が行う。
    ///
    /// (map, action) 単位の橋渡し(<see cref="IInputController.Bind{TValue}"/>)は利用者側の enum に
    /// 依存するため、ここでは行わない。生成された派生クラスの Initialize に書くこと。
    /// </summary>
    [InitializeOrder(InitializeOrderConst.InitializerEarly)]
    public abstract class InputInitializerBase : InitializerBase
    {
        [SerializeField] private InputDispatcher _inputDispatcher;

        [SerializeReference]
        [SubclassSelector]
        [Tooltip("InputState を生成して BlackBoard へ登録する Application クラス。")]
        private IInputManager _inputManager = new InputManager();

        /// <summary>
        /// 入力の操作面。生成された派生クラスが Awake で DI コンテナへ登録する。
        /// Initialize より前でも参照できるが、実際に操作できるのは Initialize 以降になる。
        /// </summary>
        protected IInputController Controller => _inputManager;

        /// <summary>
        /// InputDispatcher を初期化したうえで、Application に InputState を用意させる。
        /// </summary>
        /// <param name="blackBoard">InputState の登録先</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            if (_inputDispatcher == null)
            {
                UsefulLogger.LogError("InputDispatcher が設定されていない為、入力システムを初期化できません。", this);
                return;
            }

            if (_inputManager == null)
            {
                UsefulLogger.LogError("IInputManager が設定されていない為、InputState を生成できません。", this);
                return;
            }

            // InputState は接続時に現在の内容をエンジンへ押し込むため、
            // InputActionAsset を扱える状態にしてから Application の初期化を行う
            _inputDispatcher.Initialize();

            // --  ここにApplicationの初期化を配置。内部でInputStateを生成してBlackBoardに登録 --
            // 生成に失敗した場合は Initialized を立てずに抜ける
            if (!_inputManager.Initialize(blackBoard, _inputDispatcher))
            {
                return;
            }

            base.Initialize(blackBoard);
        }
    }
}
