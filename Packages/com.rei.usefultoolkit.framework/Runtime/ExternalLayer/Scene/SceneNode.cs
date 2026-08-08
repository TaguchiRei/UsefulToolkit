using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework
{
    [Serializable]
    public class SceneNode<T> where T : Enum
    {
        public readonly int NodeId;
        public IReadOnlyList<SceneGroupBase<T>> SceneGroups => _sceneGroups;
        public IReadOnlyList<SceneNode<T>> NextScenes => _nextScenes;

        private SceneGroupBase<T>[] _sceneGroups;
        private SceneNode<T>[] _nextScenes;

        public SceneNode(int nodeId, SceneGroupBase<T>[] sceneGroups, SceneNode<T>[] nextScenes)
        {
            NodeId = nodeId;
            _sceneGroups = sceneGroups;
            _nextScenes = nextScenes;
        }
    }
}