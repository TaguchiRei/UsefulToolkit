using UnityEngine;
using UsefulToolkit.Architecture;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace Sandbox.Initialization
{
    /// <summary>同じ型が複数個体あるときのListフィールド生成を確認するためのInitializer。</summary>
    public sealed class CompositerTestMultiple : InitializerBase
    {
        public override void Initialize(IBlackBoard blackBoard)
        {
            base.Initialize(blackBoard);
            Debug.Log($"[CompositerTest] Multiple.Initialize : {name} blackBoard={blackBoard != null}");
        }
    }
}
