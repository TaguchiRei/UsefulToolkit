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
    /// エンジンからStateへ値を押し込む経路を持たない。入力ソースの接続も
    /// <see cref="TryCreateInputSource{TValue}"/>で生成物を渡すだけで、
    /// チャンネルへの接続はInputState自身が行う。
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
        /// 指定したActionに対応するエンジン側の入力ソースを作る。
        /// 対応するActionが存在しない場合はfalseを返す。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        /// <param name="source">作られた入力ソース</param>
        bool TryCreateInputSource<TValue>(Enum map, Enum action, out IExternalInputSource<TValue> source)
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
