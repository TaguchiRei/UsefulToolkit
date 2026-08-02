using System;

namespace UsefulToolkit.Framework
{
    public sealed class FlexibleSceneGroup<T> : SceneGroupBase where T : Enum
    {
        public T LightingScene { get; }
        public T ContentScene { get; }
        public T LogicScene { get; }

        public FlexibleSceneGroup(T lightingScene, T contentScene, T logicScene)
        {
            LightingScene = lightingScene;
            ContentScene = contentScene;
            LogicScene = logicScene;
        }
    }
}