using UnityEngine;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// UsefulToolkitSettingsの各ページのベースクラス
    /// </summary>
    public abstract class SettingPageBase : IInitializable
    {
        /// <summary>
        /// タブに表示される名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 並び順（小さい方が先頭）
        /// </summary>
        public virtual int Order => 0;

        /// <summary>
        /// GUIの描画処理
        /// </summary>
        public abstract void OnGUI();

        /// <summary>
        /// 初期化処理（必要に応じてオーバーライド）
        /// </summary>
        public virtual void Initialize()
        {
        }
    }
}