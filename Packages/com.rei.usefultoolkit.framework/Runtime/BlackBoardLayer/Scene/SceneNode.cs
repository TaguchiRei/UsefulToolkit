using System;

namespace UsefulToolkit.Framework
{
    [Serializable]
    public class SceneNode<T> where T : Enum
    {
        public SceneGroup<T>[] SceneGroups;

        public SceneNode<T>[] NextScenes;
    }
}