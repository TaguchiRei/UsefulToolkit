using UnityEngine;
using UsefulToolkit.Architecture;

namespace Sandbox.Initialization
{
    /// <summary>同じ型が複数個体あるときのListフィールド生成を確認するためのInitializer。</summary>
    public sealed class CompositerTestMultiple : InitializerBase
    {
        public override void Initialize()
        {
            base.Initialize();
            Debug.Log($"[CompositerTest] Multiple.Initialize : {name}");
        }
    }
}
