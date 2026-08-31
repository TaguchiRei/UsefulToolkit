using UnityEngine;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// 常駐シーン(<see cref="PersistentSceneCreator"/> / UsefulToolkit/Scene/GenerateUsefulPersistentScene)の生成時に、
    /// パッケージ固有のコンポーネントを "UsefulToolkit System" ルートへ追加するための拡張点。
    ///
    /// framework は input などの上位パッケージを参照できない(依存方向が逆になる)ため、
    /// 各パッケージの Editor アセンブリがこの interface を実装し、
    /// <see cref="PersistentSceneCreator"/> が TypeCache で発見して呼び出す。
    /// 発見対象になるには、引数なしのコンストラクタを持つ具象クラスであること。
    /// </summary>
    public interface IPersistentSceneContributor
    {
        /// <summary>呼び出し順。小さいほど先に呼ばれる。</summary>
        int Order { get; }

        /// <summary>
        /// 常駐シーンのルート GameObject へコンポーネントを追加する。
        /// 呼ばれる時点で <see cref="UsefulToolkit.EngineService.SceneLoader"/> と
        /// <see cref="UsefulToolkit.Initialization.UsefulToolkitRuntimeInitializer"/> は既に付いている。
        /// Compositor はまだ生成されていないため、ここで追加した
        /// <see cref="UsefulToolkit.Initialization.InitializerBase"/> は
        /// 直後に走る <see cref="GameCompositorGenerator"/> の走査対象になる。
        /// </summary>
        /// <param name="systemRoot">常駐シーンのルート GameObject</param>
        void Contribute(GameObject systemRoot);
    }
}
