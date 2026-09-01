using System;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Input;

namespace UsefulToolkit.Application.Input
{
    /// <summary>
    /// 入力機能のApplicationクラスが満たすべき契約。
    /// InputStateの生成とInputStateBoardへの登録、およびActionと入力ソースの結び付けを担う。
    ///
    /// Initializerは<c>[SerializeReference]</c>でこの型を保持するため、
    /// 実装クラスは<c>[Serializable]</c>かつ引数なしのコンストラクタを持つこと。
    /// </summary>
    public interface IInputManager : IInputBinder
    {
        /// <summary>
        /// InputStateを生成してInputStateBoardへ登録し、エンジンとの橋渡しを繋ぐ。
        /// </summary>
        /// <param name="blackBoard">InputStateの登録先</param>
        /// <param name="engineBridge">InputStateへ繋ぐエンジン側の橋渡し</param>
        void Initialize(IBlackBoard blackBoard, IInputEngineBridge engineBridge);
    }

    /// <summary>
    /// Actionと入力ソースを結び付けるための操作面。DIコンテナ経由で各シーンへ配る。
    /// 生成されたActionMaps・XxxActionsのenumに依存する処理は、この面を通して利用側が行う。
    /// </summary>
    public interface IInputBinder
    {
        /// <summary>
        /// 指定したActionをInputStateのチャンネルへ橋渡しする。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        void Bind<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 生成済みのInputStateの操作面。Initializeが済むまではnull。
        /// </summary>
        IInputDispatcher Dispatcher { get; }
    }
}
