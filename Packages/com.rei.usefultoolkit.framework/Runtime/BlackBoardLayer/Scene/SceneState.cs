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
        /// <summary> メインシーンが未設定であることを表すシーンID </summary>
        public const int NoSceneId = -1;

        public int LoadedMainSceneId { get; private set; } = NoSceneId;
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

        /// <summary>
        /// 指定したシーンがロードされた時に実行するアクションを登録する。
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="loadedAction">登録するアクション</param>
        /// <param name="invokeOnAlreadyLoaded">trueなら、既にロード済みの場合はその場で実行する</param>
        /// <exception cref="ArgumentNullException">loadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false)
        {
            var list = GetOrCreateActionList(_loadedActions, sceneId);
            ThrowIfCannotRegister(list, loadedAction, nameof(loadedAction));

            if (invokeOnAlreadyLoaded && IsLoaded(sceneId))
            {
                loadedAction.Invoke();
                if (loadedAction.DisposeOnUsed)
                {
                    return BoardDispose.Empty;
                }
            }

            list.Add(loadedAction);
            return new BoardDispose(() => list.Remove(loadedAction));
        }

        /// <summary>
        /// 指定したシーンがアンロードされた時に実行するアクションを登録する。
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="unloadedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">unloadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction)
        {
            var list = GetOrCreateActionList(_unLoadedActions, sceneId);
            ThrowIfCannotRegister(list, unloadedAction, nameof(unloadedAction));

            list.Add(unloadedAction);
            return new BoardDispose(() => list.Remove(unloadedAction));
        }

        /// <summary>
        /// どのシーンがロードされた時でも実行するアクションを登録する。
        /// </summary>
        /// <param name="loadedAction">登録するアクション</param>
        /// <exception cref="ArgumentNullException">loadedActionにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction)
        {
            ThrowIfCannotRegister(_anySceneLoadedActions, loadedAction, nameof(loadedAction));

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

            // アクションから見た時に実際のロード状況と食い違わないよう、Stateを更新してから通知する
            var previousMainSceneId = LoadedMainSceneId;
            LoadedMainSceneId = sceneId;

            if (previousMainSceneId != NoSceneId)
            {
                CheckUnLoadedActions(previousMainSceneId);
            }

            CheckLoadedActions(sceneId);
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

            _loadedSubSceneIds.Add(sceneId);

            CheckLoadedActions(sceneId);
        }

        /// <summary>
        /// ロード済みのサブシーンをメインシーンへ昇格させ、それまでのメインシーンをサブシーンへ降格させる。
        /// 実際のシーンのロード/アンロードは発生しないため、Load/Unloadのアクションは発火しない。
        /// </summary>
        /// <param name="newMainSceneId">メインシーンへ昇格させるシーンID</param>
        public void ChangeMainScene(int newMainSceneId)
        {
            if (LoadedMainSceneId == newMainSceneId)
            {
                UsefulLogger.LogWarning($"シーンID{newMainSceneId}は既にメインシーンです。", this);
                return;
            }

            if (!_loadedSubSceneIds.Remove(newMainSceneId))
            {
                UsefulLogger.LogWarning($"シーンID{newMainSceneId}はサブシーンとしてロードされていない為、メインシーンへ変更できません。", this);
                return;
            }

            // 旧メインシーンはアンロードされる訳ではないのでサブシーンとして残す
            if (LoadedMainSceneId != NoSceneId)
            {
                _loadedSubSceneIds.Add(LoadedMainSceneId);
            }

            LoadedMainSceneId = newMainSceneId;
        }

        /// <summary>
        /// サブシーンをアンロードする
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        public void UnLoadSubScene(int sceneId)
        {
            if (!_loadedSubSceneIds.Remove(sceneId))
            {
                if (LoadedMainSceneId == sceneId)
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はメインシーンの為、サブシーンとしてアンロードできません。", this);
                }
                else
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はサブシーンとしてロードされていません。", this);
                }

                return;
            }

            // アクションから見た時に実際のロード状況と食い違わないよう、Stateを更新してから通知する
            CheckUnLoadedActions(sceneId);
        }

        #endregion

        public override string GetLog()
        {
            var allList = new List<int>(_loadedSubSceneIds);
            allList.Insert(0, LoadedMainSceneId);
            return string.Join("\n", allList);
        }

        /// <summary>
        /// 指定シーンのアクションリストを取得する。無ければ作成して辞書へ登録する。
        /// </summary>
        private static List<ActionEntry> GetOrCreateActionList(Dictionary<int, List<ActionEntry>> actions, int sceneId)
        {
            if (!actions.TryGetValue(sceneId, out var list))
            {
                list = new List<ActionEntry>();
                actions[sceneId] = list;
            }

            return list;
        }

        /// <summary>
        /// 登録できないActionEntryを弾く。
        /// 同じアクションを2回登録すると、解除時にどちらの登録なのか区別できず、
        /// 片方をDisposeした時にもう片方が消える事故になるため登録時点で弾く。
        /// </summary>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションが既に登録されているときに出力</exception>
        private static void ThrowIfCannotRegister(List<ActionEntry> registeredActions, ActionEntry entry,
            string paramName)
        {
            if (!entry.HasAction)
            {
                throw new ArgumentNullException(paramName, "ActionEntryに実行するActionが設定されていません。");
            }

            if (registeredActions.Contains(entry))
            {
                throw new InvalidOperationException($"アクション [{entry.ActionName}] はすでに登録されています。");
            }
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