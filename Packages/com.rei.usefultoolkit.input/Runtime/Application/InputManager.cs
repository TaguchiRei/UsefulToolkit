using System;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.Application.Input
{
    /// <summary>
    /// <see cref="IInputManager"/>の既定実装。
    /// InputStateを生成して所有し、InputBoardへ<see cref="IInputState"/>として登録する。
    ///
    /// 具象のInputStateを保持するのはこのクラスだけなので、Stateを変更できるのも
    /// エンジンとの接続を張り替えられるのもこのクラスに限られる。
    /// <see cref="IInputController"/>の各メソッドはそのStateへの委譲になる。
    /// </summary>
    [Serializable]
    public class InputManager : IInputManager
    {
        private InputState _inputState;

        /// <summary>
        /// InputStateを生成してInputBoardへ登録し、エンジンとの橋渡しを繋ぐ。
        /// InputBoardが未登録の場合は何も生成せずエラーログを出す。
        /// </summary>
        /// <param name="blackBoard">InputStateの登録先</param>
        /// <param name="engineBridge">InputStateへ繋ぐエンジン側の橋渡し</param>
        /// <returns>生成と登録に成功した場合はtrue。失敗した場合はfalseで、InputStateは生成されない</returns>
        public virtual bool Initialize(IBlackBoard blackBoard, IInputEngineBridge engineBridge)
        {
            if (engineBridge == null)
            {
                UsefulLogger.LogError("エンジンとの橋渡しが渡されていない為、InputStateを生成できません。", this);
                return false;
            }

            if (!blackBoard.TryGetStateBoard<InputBoard>(out var inputBoard))
            {
                UsefulLogger.LogError(
                    "InputBoard がBlackBoardに登録されていない為、InputStateを登録できません。" +
                    "常駐シーンのRoot Compositorを再生成してください。", this);
                return false;
            }

            _inputState = new InputState();
            _inputState.RegisterInputEngine(engineBridge);

            inputBoard.RegisterGameState<IInputState>(_inputState);
            return true;
        }

        #region IInputController実装 : 所有しているInputStateへの委譲

        public void SwitchActionMap(Enum map)
        {
            if (!TryGetState(nameof(SwitchActionMap))) return;

            _inputState.SwitchActionMap(map);
        }

        public void EnableActionMap(Enum map)
        {
            if (!TryGetState(nameof(EnableActionMap))) return;

            _inputState.EnableActionMap(map);
        }

        public void DisableActionMap(Enum map)
        {
            if (!TryGetState(nameof(DisableActionMap))) return;

            _inputState.DisableActionMap(map);
        }

        public void EnableInput()
        {
            if (!TryGetState(nameof(EnableInput))) return;

            _inputState.EnableInput();
        }

        public void DisableInput()
        {
            if (!TryGetState(nameof(DisableInput))) return;

            _inputState.DisableInput();
        }

        public void Bind<TValue>(Enum map, Enum action) where TValue : unmanaged
        {
            if (!TryGetState(nameof(Bind))) return;

            _inputState.Bind<TValue>(map, action);
        }

        public IDisposable RegisterExternalInputSource<TValue>(Enum map, Enum action,
            IExternalInputSource<TValue> source) where TValue : unmanaged
        {
            if (!TryGetState(nameof(RegisterExternalInputSource))) return BoardDispose.Empty;

            return _inputState.RegisterExternalInputSource(map, action, source);
        }

        #endregion

        /// <summary>
        /// InputStateが生成済みか調べる。未生成ならエラーログを出す。
        /// </summary>
        /// <param name="methodName">エラーログに含める呼び出し元のメソッド名</param>
        private bool TryGetState(string methodName)
        {
            if (_inputState != null) return true;

            UsefulLogger.LogError(
                $"InputStateが未生成の為、{methodName} を実行できません。" +
                "InputInitializerのInitializeより後に呼んでください。", this);

            return false;
        }
    }
}
