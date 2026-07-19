using UnityEngine;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 自動生成のタイミング
    /// </summary>
    public enum GenerateTiming
    {
        None = 0,
        OnAssetChanged = 1, // アセット変更時（SceneList変更やInputActions更新時）
        OnToolUpdate = 2, // 関連ツール（SceneLoaderなど）の更新時にも連動
    }
}