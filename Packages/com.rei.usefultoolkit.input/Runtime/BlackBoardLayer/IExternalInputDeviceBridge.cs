using System;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// 外部入力を仮想デバイスへ書き込む橋渡し。
    ///
    /// InputSystem の外から来る値(タッチの意味づけ、AI の入力、ネットワーク越しの入力など)は
    /// この橋渡しを通して仮想デバイスへ書き込む。書き込まれた値は通常の InputAction として
    /// 発火するため、ActionMap の有効・無効やリバインドといった下流の仕組みがそのまま効く。
    ///
    /// 実装は <c>UsefulToolkit/Input/Generate External Input Device</c> が
    /// 利用者のアセンブリへ生成する。仮想デバイスの型はパッケージからは参照できないため、
    /// 生成された Initializer が実装を渡す(<see cref="UsefulToolkit.Initialization.InputInitializerBase"/>)。
    /// </summary>
    public interface IExternalInputDeviceBridge
    {
        /// <summary>
        /// 指定したスロットへ値を書き込む。
        /// スロットが存在しない、値の型が宣言と食い違う、デバイスが未生成の場合は
        /// エラーログを出して false を返す。
        /// </summary>
        /// <param name="slot">書き込み先のスロットを表すenum(生成された ExternalInputs)</param>
        /// <param name="value">書き込む値</param>
        /// <returns>書き込めた場合はtrue</returns>
        bool TryWrite<TValue>(Enum slot, TValue value) where TValue : unmanaged;
    }
}
