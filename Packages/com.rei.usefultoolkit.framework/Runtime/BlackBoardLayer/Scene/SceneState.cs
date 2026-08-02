using System;
using System.Collections.Generic;
using UsefulToolkit.Application.StateManagement;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// アクティブシーンと、Additiveでロード中の全シーン名を持つ。
    /// </summary>
    public interface ISceneStateGetter<T> : IStateGetter
    {
        
    }

    /// <summary>
    /// 現在のシーンおよび次のシーンの取得、シーン変更時の条件取得などを行うクラス
    /// </summary>
    public sealed class SceneState<T> : GameStateBase, ISceneStateGetter<T>
    {
        
    }
}