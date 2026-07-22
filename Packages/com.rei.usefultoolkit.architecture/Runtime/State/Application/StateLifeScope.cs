using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// ステートの生存時間を規定するenum
    /// </summary>
    public enum StateLifeScope
    {
        /// <summary> ゲーム終了時まで存在する </summary>
        OnGameEnd,

        /// <summary> シーン終了時まで存在する </summary>
        OnSceneEnd,

        /// <summary> 自動破棄されず、手動UnRegisterが必要 </summary>
        Other
    }
}