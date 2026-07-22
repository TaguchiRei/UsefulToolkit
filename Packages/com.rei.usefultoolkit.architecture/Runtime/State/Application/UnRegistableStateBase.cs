using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// 生存時間を制御可能なステートのベースクラス
    /// </summary>
    public class UnRegistableStateBase : StateBase
    {
        public override StateLifeScope LifeScope => StateLifeScope.Other;
    }
}