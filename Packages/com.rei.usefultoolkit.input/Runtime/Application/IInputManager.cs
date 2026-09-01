using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;

namespace UsefulToolkit.Application.Input
{
    /// <summary>
    /// 入力機能のApplicationクラスが満たすべき契約。
    /// InputStateの生成とInputBoardへの登録を担う。
    ///
    /// Initializerは<c>[SerializeReference]</c>でこの型を保持するため、
    /// 実装クラスは<c>[Serializable]</c>かつ引数なしのコンストラクタを持つこと。
    /// </summary>
    public interface IInputManager
    {
        /// <summary>
        /// InputStateを生成してInputBoardへ登録し、エンジンとの橋渡しを繋ぐ。
        /// </summary>
        /// <param name="blackBoard">InputStateの登録先</param>
        /// <param name="engineBridge">InputStateへ繋ぐエンジン側の橋渡し</param>
        void Initialize(IBlackBoard blackBoard, IInputEngineBridge engineBridge);
    }
}
