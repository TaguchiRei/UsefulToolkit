using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.Input
{
    /// <summary>
    /// <see cref="InputState"/>の内容をゲームエンジン側へ反映し、現在の入力値を読み出すための橋渡し。
    /// EngineServiceLayerのクラスが実装し、Stateを生成したクラスが
    /// <see cref="InputState.RegisterInputEngine"/>で繋ぐ。
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
        /// 指定したActionをエンジン側の入力ソースとしてInputStateのチャンネルへ繋ぐ。
        /// 張ったブリッジの解除はこの実装側が持つ。
        /// </summary>
        /// <param name="map">ActionMapを表すenum</param>
        /// <param name="action">Actionを表すenum</param>
        void BindAction<TValue>(Enum map, Enum action) where TValue : unmanaged;

        /// <summary>
        /// 有効なActionMapを指定された内容へ揃える。列挙に含まれないActionMapは無効化する。
        /// </summary>
        /// <param name="activeActionMaps">有効にするActionMap名</param>
        void ApplyActiveActionMaps(IReadOnlyList<string> activeActionMaps);

        /// <summary>
        /// 入力全体の有効・無効を反映する。
        /// </summary>
        /// <param name="inputEnabled">入力を受け付けるか</param>
        void ApplyInputEnabled(bool inputEnabled);
    }
}
