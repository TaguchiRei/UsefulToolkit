using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// InputStateへ入力を橋渡しする入力ソースの契約。EngineServiceLayerに属するクラス
    /// (InputDispatcher/MobileInputEngineServiceおよび利用者が追加する外部入力ソース)が実装し、
    /// IInputDispatcher.RegisterExternalInputSourceで登録する。チャンネルへの値の流し込みは
    /// InputState自身のブリッジだけが行うため、このインターフェースを実装するだけでは発行権限を持たない。
    /// </summary>
    public interface IExternalInputSource<TValue> where TValue : unmanaged
    {
        void RegisterAction(Action<InputContext<TValue>> handler);
        void UnRegisterAction(Action<InputContext<TValue>> handler);
    }
}
