using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.BlackBoard
{
    [RegisterBoard(typeof(SceneBoard))]
    public class SceneState : GameStateBase, ISceneState
    {
        public int LoadedMainSceneId { get; private set; }
        public IReadOnlyList<int> LoadedSubSceneIds => _loadedSubSceneIds;
        private List<int> _loadedSubSceneIds = new();


        public bool IsLoaded(int sceneId)
        {
            if (_loadedSubSceneIds.Contains(sceneId) || LoadedMainSceneId == sceneId)
            {
                return true;
            }

            return false;
        }

        public IDisposable RegisterEventOnLoad(Action loadedAction, bool invokeOnAlreadyLoaded = false)
        {
            throw new NotImplementedException();
        }

        public IDisposable RegisterEventOnUnload(Action unloadedAction)
        {
            throw new NotImplementedException();
        }

        public override string GetLog()
        {
            var allList = new List<int>(_loadedSubSceneIds);
            allList.Insert(0, LoadedMainSceneId);
            return string.Join("\n", allList);
        }
    }

    public interface ISceneState : IStateGetter
    {
        public int LoadedMainSceneId { get; }
        public IReadOnlyList<int> LoadedSubSceneIds { get; }

        public bool IsLoaded(int sceneId);
        public IDisposable RegisterEventOnLoad(Action loadedAction, bool invokeOnAlreadyLoaded = false);
        public IDisposable RegisterEventOnUnload(Action unloadedAction);
    }
}