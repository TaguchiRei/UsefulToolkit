using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// シーンアンロードまで残るステートのベースクラス
    /// </summary>
    public class SceneStateBase : StateBase
    {
        public override StateLifeScope LifeScope => StateLifeScope.OnSceneEnd;
    }
}