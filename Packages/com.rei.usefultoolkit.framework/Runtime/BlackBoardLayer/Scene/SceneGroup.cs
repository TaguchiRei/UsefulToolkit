using System;

namespace UsefulToolkit.Framework
{
    [Serializable]
    public sealed class SceneGroup<T> : SceneGroupBase where T : Enum
    {
        public T LightingScene { get; }
        public T ContentScene { get; }
        public T LogicScene { get; }

        public SceneGroup(T lightingScene, T contentScene, T logicScene)
        {
            LightingScene = lightingScene;
            ContentScene = contentScene;
            LogicScene = logicScene;
        }
    }
}