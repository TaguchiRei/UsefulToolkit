using UnityEngine;
using UsefulToolkit.Architecture;

namespace Sandbox.Initialization
{
    /// <summary>依存を提供する側。Awakeで自分自身をCompositerへ登録する。</summary>
    public sealed class CompositerTestProvider : InitializerBase
    {
        private void Awake()
        {
            bool registered = GameCompositer.TryRegisterContent(this);
            Debug.Log($"[CompositerTest] Provider登録 : {registered}");
        }

        public override void Initialize()
        {
            base.Initialize();
            Debug.Log("[CompositerTest] Provider.Initialize");
        }
    }
}
