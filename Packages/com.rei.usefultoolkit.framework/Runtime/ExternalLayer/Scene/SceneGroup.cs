using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// 1つのノードで同時に読み込まれるシーンの組み合わせ。
    /// SceneFlowAssetのBuildが生成する実行時表現で、生成後は不変。
    /// enumからシーン名への変換はBuildの時点で済ませてあるため、この型より先にenumは出てこない。
    /// </summary>
    public sealed class SceneGroup
    {
        /// <summary> このグループが読み込みを必要とするシーン名の一覧 </summary>
        public IReadOnlyList<string> Scenes { get; }

        public SceneGroup(IReadOnlyList<string> scenes)
        {
            Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        }
    }
}
