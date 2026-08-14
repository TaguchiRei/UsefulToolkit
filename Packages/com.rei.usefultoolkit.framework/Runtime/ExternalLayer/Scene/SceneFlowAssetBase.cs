using UnityEngine;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// SceneFlowAssetの非ジェネリックな基底クラス。
    /// シーンenumの型引数を知らない側がアセットを受け取るときはこの型を使う。
    /// </summary>
    public abstract class SceneFlowAssetBase : ScriptableObject
    {
        /// <summary>
        /// インスペクタで組んだ内容を実行時表現へ変換する。Initialization層で一度だけ呼ぶ想定。
        /// </summary>
        public abstract SceneFlow Build();
    }
}
