using UnityEngine;

namespace UsefulToolkit.Framework.Initialization
{
    public abstract class InitializerBase : MonoBehaviour
    {
        public bool Initialized { get; internal set; } = false;

        public virtual void Initialize()
        {
            Initialized = true;
        }
    }
}