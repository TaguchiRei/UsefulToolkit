using System;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    public abstract class SceneFlowBase<T> : ScriptableObject where T : Enum
    {
        public SceneNode<T>[] SceneNodes;
    }
}