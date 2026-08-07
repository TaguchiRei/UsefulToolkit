using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// シーンアンロードまで残るステートのベースクラス
    /// </summary>
    public class SceneStateBase : StateBase
    {
        public sealed override StateLifeScope LifeScope => StateLifeScope.OnSceneEnd;
    }
}