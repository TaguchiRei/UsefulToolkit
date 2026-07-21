using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// ステートのフェーズを表す
    /// </summary>
    public enum StatePhase
    {
        /// <summary> そのステートに変化した瞬間 </summary>
        Start,

        /// <summary> そのステートが継続している状態 </summary>
        Stay,

        /// <summary> そのステートから別のステートに変化した瞬間 </summary>
        End
    }
}
