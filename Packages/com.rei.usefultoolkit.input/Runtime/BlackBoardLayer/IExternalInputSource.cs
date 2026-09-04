using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// InputStateへ入力を橋渡しする入力ソースの契約。EngineAdapterLayerに属するクラス
    /// (InputDispatcher/MobileInputEngineAdapterおよび利用者が追加する外部入力ソース)が実装し、
    /// IInputController.RegisterExternalInputSourceで登録する。チャンネルへの値の流し込みは
    /// InputState自身のブリッジだけが行うため、このインターフェースを実装するだけでは発行権限を持たない。
    ///
    /// 1つの入力ソースは複数の(map, action)へ登録されうるため、ハンドラは多重に保持できること。
    /// 実装は次を満たすこと。
    /// ・RegisterActionは渡されたハンドラを追加する。既存のハンドラを置き換えてはならない。
    /// ・UnRegisterActionは渡されたハンドラだけを取り除く。他のハンドラは残す。
    /// ・handlerがnullの場合、およびUnRegisterActionに未登録のハンドラが渡された場合は何もしない。
    /// 同じハンドラを2回登録した場合の扱いは規定しないため、登録側が重複させないこと。
    /// </summary>
    public interface IExternalInputSource<TValue> where TValue : unmanaged
    {
        /// <summary>
        /// 入力時に実行するハンドラを追加する。
        /// </summary>
        /// <param name="handler">追加するハンドラ</param>
        void RegisterAction(Action<InputContext<TValue>> handler);

        /// <summary>
        /// 追加済みのハンドラを取り除く。
        /// </summary>
        /// <param name="handler">取り除くハンドラ</param>
        void UnRegisterAction(Action<InputContext<TValue>> handler);
    }
}
