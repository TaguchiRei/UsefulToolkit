using UnityEngine;

namespace UsefulToolkit.Attributes
{
    /// <summary>
    /// string フィールドを「プロジェクト内の全シーンから 1 つ選ぶプルダウン」として描画する。
    /// 選択結果はシーンアセットのパス(Assets/…/Foo.unity)で保存される。空欄は「指定なし」。
    ///
    /// 候補は <see cref="UnityEditor.AssetDatabase"/> から描画時に列挙するため、
    /// 別途の生成物やその同期は不要。Build Settings 登録の有無は問わない。
    /// </summary>
    public class ScenePopupAttribute : PropertyAttribute
    {
    }
}
