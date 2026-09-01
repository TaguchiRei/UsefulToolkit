using System;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.Application.Input
{
    /// <summary>
    /// <see cref="IInputManager"/>の既定実装。
    /// InputStateを生成して所有し、InputStateBoardへ<see cref="IInputState"/>として登録する。
    ///
    /// 具象のInputStateを保持するのはこのクラスだけなので、
    /// エンジンとの接続を張り替えられるのもこのクラスに限られる。
    /// </summary>
    [Serializable]
    public class InputManager : IInputManager
    {
        private InputState _inputState;

        /// <summary>
        /// InputStateを生成してInputStateBoardへ登録し、エンジンとの橋渡しを繋ぐ。
        /// InputStateBoardが未登録の場合は何も生成せずエラーログを出す。
        /// </summary>
        /// <param name="blackBoard">InputStateの登録先</param>
        /// <param name="engineBridge">InputStateへ繋ぐエンジン側の橋渡し</param>
        public virtual void Initialize(IBlackBoard blackBoard, IInputEngineBridge engineBridge)
        {
            if (engineBridge == null)
            {
                UsefulLogger.LogError("エンジンとの橋渡しが渡されていない為、InputStateを生成できません。", this);
                return;
            }

            if (!blackBoard.TryGetStateBoard<InputStateBoard>(out var inputStateBoard))
            {
                UsefulLogger.LogError(
                    "InputStateBoard がBlackBoardに登録されていない為、InputStateを登録できません。" +
                    "常駐シーンのRoot Compositorを再生成してください。", this);
                return;
            }

            _inputState = new InputState();
            _inputState.RegisterInputEngine(engineBridge);

            inputStateBoard.RegisterGameState<IInputState>(_inputState);
        }
    }
}
