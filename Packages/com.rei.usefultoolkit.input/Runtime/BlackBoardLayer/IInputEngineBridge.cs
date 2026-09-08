using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// <see cref="InputState"/>の内容をゲームエンジン側へ反映し、現在の入力値を読み出すための橋渡し。
    /// EngineAdapterLayerのクラスが実装し、Stateを生成したクラスが
    /// <see cref="InputState.RegisterInputEngine"/>で繋ぐ。
    ///
    /// エンジンはStateの写しであって二つ目の正本ではないため、この橋渡しは
    /// エンジンからStateへ値を押し込む経路を持たない。コールバックの登録も
    /// <see cref="Subscribe{TValue}"/>でエンジン側の発火元へ直接繋ぐだけで、
    /// Stateは登録簿を持たない。
    /// </summary>
    public interface IInputEngineBridge
    {
        /// <summary>
        /// 指定したActionの現在値を読み出す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        InputContext<TValue> ReadValue<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 指定したActionの発火(started / performed / canceled)へハンドラを繋ぐ。
        /// 対応するActionが存在しない場合は、何も解除しないハンドルを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="handler">入力時に実行するハンドラ</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        IDisposable Subscribe<TValue>(Enum map, Enum action, Action<InputContext<TValue>> handler)
            where TValue : unmanaged;

        /// <summary>
        /// Stateの現在の内容をエンジンへ差分で反映する。
        /// 目標に含まれないActionMapだけを無効化し、まだ有効でないActionMapだけを有効化する。
        /// 既に有効なActionMapには触れないため、進行中の入力は中断されない。
        /// 入力が無効な間はどのActionMapも有効にしないこと。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        void Apply(bool inputEnabled, IReadOnlyList<string> activeActionMaps);

        /// <summary>
        /// Stateの現在の内容をエンジンへ反映する。全ActionMapを一度無効化してから対象だけ有効化する。
        /// 有効なままになるActionMapも張り直すため、進行中の入力は打ち切られる。
        /// ActionMapを1つへ切り替える操作など、状態を作り直したい場合に使う。
        /// 入力が無効な間はどのActionMapも有効にしないこと。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        void ApplyExclusive(bool inputEnabled, IReadOnlyList<string> activeActionMaps);
    }
}
