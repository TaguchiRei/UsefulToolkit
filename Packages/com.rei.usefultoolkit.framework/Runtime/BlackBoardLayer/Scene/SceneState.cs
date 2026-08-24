using System;
using System.Collections.Generic;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Scene;
using UnityEngine.Pool;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard
{
    [RegisterBoard(typeof(SceneBoard))]
    public class SceneState : GameStateBase, ISceneState
    {
        public int LoadedMainSceneId { get; private set; } = -1;
        public IReadOnlyList<int> LoadedSubSceneIds => _loadedSubSceneIds;
        private readonly List<int> _loadedSubSceneIds = new();

        private readonly Dictionary<int, List<ActionEntry>> _loadedActions = new();
        private readonly Dictionary<int, List<ActionEntry>> _unLoadedActions = new();
        private readonly List<ActionEntry> _anySceneLoadedActions = new();

        #region ISceneState実装

        public bool IsLoaded(int sceneId)
        {
            if (_loadedSubSceneIds.Contains(sceneId) || LoadedMainSceneId == sceneId)
            {
                return true;
            }

            return false;
        }

        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false)
        {
            if (invokeOnAlreadyLoaded && IsLoaded(sceneId))
            {
                loadedAction.Invoke();
                if (loadedAction.DisposeOnUsed)
                {
                    return BoardDispose.Empty;
                }
            }

            if (!_loadedActions.TryGetValue(sceneId, out var list))
            {
                list = new List<ActionEntry>();
                _loadedActions[sceneId] = list;
            }

            list.Add(loadedAction);
            return new BoardDispose(() => list.Remove(loadedAction));
        }

        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction)
        {
            if (!_unLoadedActions.TryGetValue(sceneId, out var list))
                _unLoadedActions[sceneId] = new List<ActionEntry>();

            _unLoadedActions[sceneId].Add(unloadedAction);
            return new BoardDispose(() => list.Remove(unloadedAction));
        }

        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction)
        {
            _anySceneLoadedActions.Add(loadedAction);
            return new BoardDispose(() => _anySceneLoadedActions.Remove(loadedAction));
        }

        #endregion

        #region 実体を保持するクラスが利用できるメソッド

        /// <summary>
        /// メインシーンをロードする
        /// </summary>
        /// <param name="sceneId"></param>
        public void LoadMainScene(int sceneId)
        {
            if (LoadedMainSceneId == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はメインシーンとしてロード済みです", this);
                return;
            }
            else if (_loadedSubSceneIds.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はサブシーンとしてロード済みの為、メインシーンとしてロードできません", this);
                return;
            }


            CheckUnLoadedActions(LoadedMainSceneId);
            CheckLoadedActions(sceneId);

            LoadedMainSceneId = sceneId;
        }

        /// <summary>
        /// サブシーンをロードする
        /// </summary>
        /// <param name="sceneId"></param>
        public void LoadSubScene(int sceneId)
        {
            if (_loadedSubSceneIds.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はサブシーンとしてロード済みです。", this);
                return;
            }
            else if (LoadedMainSceneId == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はメインシーンとしてロード済みの為、サブシーンとして読み込めません。", this);
                return;
            }

            CheckLoadedActions(sceneId);

            _loadedSubSceneIds.Add(sceneId);
        }

        public void ChangeMainScene(int newMainSceneId)
        {
            
        }

        public void UnLoadSubScene(int sceneId)
        {
        }

        #endregion

        public override string GetLog()
        {
            var allList = new List<int>(_loadedSubSceneIds);
            allList.Insert(0, LoadedMainSceneId);
            return string.Join("\n", allList);
        }

        private void CheckLoadedActions(int sceneId)
        {
            if (_loadedActions.TryGetValue(sceneId, out var loadAction))
            {
                InvokeActionEntries(loadAction);
            }

            if (_anySceneLoadedActions.Count != 0)
            {
                InvokeActionEntries(_anySceneLoadedActions);
            }
        }

        private void CheckUnLoadedActions(int unloadedSceneId)
        {
            if (_unLoadedActions.TryGetValue(unloadedSceneId, out var unLoadAction))
            {
                InvokeActionEntries(unLoadAction);
            }
        }

        private void InvokeActionEntries(List<ActionEntry> loadAction)
        {
            List<ActionEntry> temporaryList = CollectionPool<List<ActionEntry>, ActionEntry>.Get();
            CopyListContents(loadAction, ref temporaryList);
            for (int i = 0; i < temporaryList.Count; i++)
            {
                temporaryList[i].Invoke();
                if (temporaryList[i].DisposeOnUsed)
                {
                    loadAction.Remove(temporaryList[i]);
                }
            }

            CollectionPool<List<ActionEntry>, ActionEntry>.Release(temporaryList);
        }

        /// <summary>
        /// リストの内容を複製する。
        /// </summary>
        /// <param name="baseList">コピー元のリスト</param>
        /// <param name="copy">複製を入れるリスト</param>
        private void CopyListContents(List<ActionEntry> baseList, ref List<ActionEntry> copy)
        {
            copy.Clear();
            copy.AddRange(baseList);
        }

        /// <summary>
        /// リストの内容を複製する
        /// </summary>
        /// <param name="baseList"></param>
        /// <param name="copy"></param>
        private void CopyListContents(List<Action<int[], int[]>> baseList, ref List<Action<int[], int[]>> copy)
        {
            copy.Clear();
            copy.AddRange(baseList);
        }
    }

    public interface ISceneState : IStateGetter
    {
        public int LoadedMainSceneId { get; }
        public IReadOnlyList<int> LoadedSubSceneIds { get; }

        public bool IsLoaded(int sceneId);

        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false);

        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction);

        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction);
    }
}