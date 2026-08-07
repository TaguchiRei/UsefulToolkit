using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework
{
    [Serializable]
    public sealed class SceneGroup<T> : SceneGroupBase<T> where T : Enum
    {
        public readonly int SceneId;
        public T LightingScene { get; }
        public T ContentScene { get; }
        public T LogicScene { get; }

        public override IReadOnlyList<T> Scenes => new[] { LightingScene, ContentScene, LogicScene };

        public SceneGroup(int sceneId, T lightingScene, T contentScene, T logicScene)
        {
            SceneId = sceneId;
            LightingScene = lightingScene;
            ContentScene = contentScene;
            LogicScene = logicScene;
        }
    }
}