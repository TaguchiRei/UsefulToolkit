using UnityEngine;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// SceneFlowAssetの非ジェネリックな基底クラス。
    /// SceneFlowAssetはシーンenumを型引数に取るため、そのままでは型引数を知らない側から掴めない。
    /// ノードエディタや、利用側のInitialization層がアセットを受け取るときはこの型を使う。
    /// </summary>
    public abstract class SceneFlowAssetBase : ScriptableObject
    {
        /// <summary>
        /// インスペクタ(またはノードエディタ)で組んだ内容を実行時表現へ変換する。
        /// Initialization層で一度だけ呼ぶ想定。
        /// </summary>
        public abstract SceneFlow Build();
    }
}
