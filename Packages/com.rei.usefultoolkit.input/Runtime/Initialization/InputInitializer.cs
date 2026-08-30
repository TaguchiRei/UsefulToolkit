using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.EngineService.Input;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// 入力システムを初期化する Initializer。
    ///
    /// Root Compositor が <see cref="InputBoard"/> を BlackBoard へ登録済みである前提で、
    /// そのインスタンスを取り出して <see cref="InputEngineService"/> へ渡し、初期化する。
    /// InputBoard の切替イベントは、InputEngineService の初期化が済むまで届かない設計のため、
    /// この Initializer は Application 側が ActionMap を切り替えるよりも前に走る必要がある。
    /// 常駐シーンへ置く想定で、生成される Root Compositor の InitializeAll から呼ばれる。
    ///
    /// (map, action) 単位のブリッジ生成(<see cref="InputEngineService.Bind{TValue}"/>)は、
    /// 利用者側の enum に依存するためここでは行わない。ゲームシーン側の Initializer から呼ぶこと。
    /// </summary>
    public sealed class InputInitializer : InitializerBase
    {
        [SerializeField] private InputEngineService _inputEngineService;

        /// <summary>
        /// BlackBoard から <see cref="InputBoard"/> を取得して <see cref="InputEngineService"/> へ接続し、
        /// エンジンサービスを初期化する。
        /// </summary>
        /// <param name="blackBoard">このシーンの BlackBoard</param>
        public override void Initialize(IBlackBoard blackBoard)
        {
            if (_inputEngineService == null)
            {
                UsefulLogger.LogError("InputEngineService が設定されていない為、入力システムを初期化できません。", this);
                base.Initialize(blackBoard);
                return;
            }

            if (!blackBoard.TryGetEventBoard<InputBoard>(out var inputBoard))
            {
                UsefulLogger.LogError(
                    "InputBoard が BlackBoard に登録されていません。" +
                    "常駐シーンの Root Compositor が生成・配置されているか確認してください。", this);
                base.Initialize(blackBoard);
                return;
            }

            _inputEngineService.SetInputBoard(inputBoard);
            _inputEngineService.Initialize();

            base.Initialize(blackBoard);
        }
    }
}
