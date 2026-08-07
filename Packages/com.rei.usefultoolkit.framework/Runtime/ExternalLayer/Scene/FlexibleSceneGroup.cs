using System;
using System.Collections.Generic;
using System.Linq;

namespace UsefulToolkit.Framework
{
    public sealed class FlexibleSceneGroup<T> : SceneGroupBase<T> where T : Enum
    {
        public T LightingScene { get; }
        public T ContentScene { get; }
        public T LogicScene { get; }
        public IReadOnlyList<T> AdditionalScenes => _additionalScenes;
        private T[] _additionalScenes { get; }

        public override IReadOnlyList<T> Scenes =>
            new[] { LightingScene, ContentScene, LogicScene }.Concat(_additionalScenes).ToArray();

        public FlexibleSceneGroup(T lightingScene, T contentScene, T logicScene, T[] additionalScenes)
        {
            LightingScene = lightingScene;
            ContentScene = contentScene;
            LogicScene = logicScene;
            _additionalScenes = additionalScenes;
        }
    }
}