using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// InputBoardへ入力を橋渡しする入力ソースの契約。EngineServiceLayerに属するクラス
    /// (InputEngineService/MobileInputEngineServiceおよび利用者が追加する外部入力ソース)が実装し、
    /// InputBoard.RegisterExternalInputSourceで登録する。実際のEventChannel.PublishはInputBoard
    /// 自身のブリッジだけが行うため、このインターフェースを実装するだけではPublish権限は持たない。
    /// </summary>
    public interface IExternalInputSource<TValue> where TValue : unmanaged
    {
        void RegisterAction(Action<InputContext<TValue>> handler);
        void UnRegisterAction(Action<InputContext<TValue>> handler);
    }
}
