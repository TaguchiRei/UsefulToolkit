using UnityEngine;

namespace UsefulToolkit.Architecture
{
    public abstract class InitializableMonoBehaviour : MonoBehaviour
    {
        public bool Initialized { get; internal set; } = false;

        void Awake()
        {
            if (!Initialized)
            {
                enabled = false;
            }
        }

        public virtual void Initialize()
        {
            Initialized = true;
            enabled = true;
        }
    }
}
