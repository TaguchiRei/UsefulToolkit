using UnityEngine;

namespace UsefulToolkit.Architecture
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
