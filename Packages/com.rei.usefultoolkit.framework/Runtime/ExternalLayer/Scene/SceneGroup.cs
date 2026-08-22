using System;
using System.Collections.Generic;

namespace UsefulToolkit.External.Scene
{
    /// <summary> 1つのノードで同時に読み込まれるシーンの組み合わせ。生成後は不変 </summary>
    public sealed class SceneGroup
    {
        /// <summary> 読み込むシーン名の一覧。この順番で読み込まれる </summary>
        public IReadOnlyList<string> Scenes { get; }

        /// <summary>
        /// 読み込み後にアクティブシーンにするシーン名。
        /// ライティングやSkyboxの設定はこのシーンのものが使われる。
        /// </summary>
        public string MainScene { get; }

        /// <summary>
        /// trueなら遷移元と共通のシーンも読み直す。
        /// falseなら共通シーンはUnloadもLoadもされず、状態がそのまま引き継がれる。
        /// </summary>
        public bool ForceReload { get; }

        public SceneGroup(IReadOnlyList<string> scenes, string mainScene, bool forceReload)
        {
            Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            MainScene = mainScene ?? string.Empty;
            ForceReload = forceReload;
        }
    }
}
