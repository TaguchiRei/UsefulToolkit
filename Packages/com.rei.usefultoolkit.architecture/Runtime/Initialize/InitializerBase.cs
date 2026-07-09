using System;
using UnityEngine;
using UsefulToolkit.Attributes;

namespace UsefulToolkit.Architecture
{
    [DefaultExecutionOrder(100)]
    public abstract class InitializerBase : MonoBehaviour, IComparable<InitializableMonoBehaviour>
    {
        public int InitializationOrder = 0;
        [ShowOnly] public bool Initialized { get; protected set; } = false;
        
        public virtual void Initialize()
        {
            if (Initialized) return;

            Initialized = true;
        }

        public int CompareTo(InitializableMonoBehaviour other)
        {
            return InitializationOrder.CompareTo(other.InitializationOrder);
        }
    }
}
