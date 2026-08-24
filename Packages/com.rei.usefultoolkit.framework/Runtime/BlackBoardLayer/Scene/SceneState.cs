using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UnityEngine.Pool;
using UsefulToolkit.BlackBoard.Logger;

namespace UsefulToolkit.BlackBoard.Scene
{
    [RegisterBoard(typeof(SceneBoard))]
    public class SceneState : GameStateBase, ISceneState, IProgress<float>
    {
        /// <summary> アクティブシーンが未設定であることを表すシーンID </summary>
        public const int NoSceneId = -1;

        public float LoadProgress { get; set; } = 1;
        public bool IsLoading { get; set; } = false;
        public int ActiveScene { get; private set; } = NoSceneId;
        public IReadOnlyList<int> AdditiveScenes => _additiveScenes;
        private readonly List<int> _additiveScenes = new();

        private readonly Dictionary<int, List<ActionEntry>> _loadedActions = new();
        private readonly Dictionary<int, List<ActionEntry>> _unLoadedActions = new();
        private readonly List<ActionEntry> _anySceneLoadedActions = new();

        #region ISceneState実装

        public bool IsLoaded(int sceneId)
        {
            if (_additiveScenes.Contains(sceneId) || ActiveScene == sceneId)
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
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
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
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
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
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction)
        {
            ThrowIfCannotRegister(_anySceneLoadedActions, loadedAction, nameof(loadedAction));

            _anySceneLoadedActions.Add(loadedAction);
            return new BoardDispose(() => _anySceneLoadedActions.Remove(loadedAction));
        }

        #endregion

        #region 実体を保持するクラスが利用できるメソッド

        /// <summary>
        /// 複数のシーンを一気にロードする
        /// </summary>
        /// <param name="activeScene"></param>
        /// <param name="additiveScenes"></param>
        public void LoadMultiScene(int activeScene, int[] additiveScenes)
        {
            LoadActiveScene(activeScene);
            LoadAdditiveScenes(additiveScenes);
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にロードする
        /// </summary>
        /// <param name="additiveScenes"></param>
        public void LoadAdditiveScenes(int[] additiveScenes)
        {
            foreach (var subScene in additiveScenes)
            {
                LoadAdditiveScene(subScene);
            }
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にアンロードする
        /// </summary>
        /// <param name="additiveScenes"></param>
        public void UnLoadAdditiveScenes(int[] additiveScenes)
        {
            foreach (var subScene in additiveScenes)
            {
                UnLoadAdditiveScene(subScene);
            }
        }

        /// <summary>
        /// 複数のアディティブシーンを一気にアンロードする
        /// </summary>
        /// <param name="additiveScenes"></param>
        public void UnLoadAdditiveScenes(ReadOnlySpan<int> additiveScenes)
        {
            foreach (var subScene in additiveScenes)
            {
                UnLoadAdditiveScene(subScene);
            }
        }

        /// <summary>
        /// すべてのアディティブシーンを一気にアンロードする
        /// </summary>
        public void ClearAdditiveScenes()
        {
            int[] loadedSubSceneIds = ArrayPool<int>.Shared.Rent(_additiveScenes.Count);

            for (int i = 0; i < _additiveScenes.Count; i++)
            {
                loadedSubSceneIds[i] = _additiveScenes[i];
            }

            UnLoadAdditiveScenes(loadedSubSceneIds.AsSpan(0, _additiveScenes.Count));

            ArrayPool<int>.Shared.Return(loadedSubSceneIds);
        }

        /// <summary>
        /// アクティブシーンをロードする
        /// </summary>
        /// <param name="sceneId"></param>
        public void LoadActiveScene(int sceneId)
        {
            if (ActiveScene == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンとしてロード済みです", this);
                return;
            }
            else if (_additiveScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロード済みの為、アクティブシーンとしてロードできません", this);
                return;
            }

            // アクションから見た時に実際のロード状況と食い違わないよう、Stateを更新してから通知する
            var previousMainSceneId = ActiveScene;
            ActiveScene = sceneId;

            if (previousMainSceneId != NoSceneId)
            {
                CheckUnLoadedActions(previousMainSceneId);
            }

            CheckLoadedActions(sceneId);
        }

        /// <summary>
        /// アディティブシーンをロードする
        /// </summary>
        /// <param name="sceneId"></param>
        public void LoadAdditiveScene(int sceneId)
        {
            if (_additiveScenes.Contains(sceneId))
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロード済みです。", this);
                return;
            }
            else if (ActiveScene == sceneId)
            {
                UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンとしてロード済みの為、アディティブシーンとして読み込めません。", this);
                return;
            }

            _additiveScenes.Add(sceneId);

            CheckLoadedActions(sceneId);
        }

        /// <summary>
        /// ロード済みのアディティブシーンをアクティブシーンへ昇格させ、それまでのアクティブシーンをアディティブシーンへ降格させる。
        /// 実際のシーンのロード/アンロードは発生しないため、Load/Unloadのアクションは発火しない。
        /// </summary>
        /// <param name="newMainSceneId">アクティブシーンへ昇格させるシーンID</param>
        public void ChangeActiveScene(int newMainSceneId)
        {
            if (ActiveScene == newMainSceneId)
            {
                UsefulLogger.LogWarning($"シーンID{newMainSceneId}は既にアクティブシーンです。", this);
                return;
            }

            if (!_additiveScenes.Remove(newMainSceneId))
            {
                UsefulLogger.LogWarning($"シーンID{newMainSceneId}はアディティブシーンとしてロードされていない為、アクティブシーンへ変更できません。", this);
                return;
            }

            // 旧アクティブシーンはアンロードされる訳ではないのでアディティブシーンとして残す
            if (ActiveScene != NoSceneId)
            {
                _additiveScenes.Add(ActiveScene);
            }

            ActiveScene = newMainSceneId;
        }

        /// <summary>
        /// アディティブシーンをアンロードする
        /// </summary>
        /// <param name="sceneId">アンロードするシーンID</param>
        public void UnLoadAdditiveScene(int sceneId)
        {
            if (!_additiveScenes.Remove(sceneId))
            {
                if (ActiveScene == sceneId)
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はアクティブシーンの為、アディティブシーンとしてアンロードできません。", this);
                }
                else
                {
                    UsefulLogger.LogWarning($"シーンID{sceneId}はアディティブシーンとしてロードされていません。", this);
                }

                return;
            }

            // アクションから見た時に実際のロード状況と食い違わないよう、Stateを更新してから通知する
            CheckUnLoadedActions(sceneId);
        }

        #endregion

        public override string GetLog()
        {
            var allList = new List<int>(_additiveScenes);
            allList.Insert(0, ActiveScene);
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
        /// 同じアクションエントリーを2回登録すると、解除時にどちらの登録なのか区別できず、
        /// 片方をDisposeした時にもう片方が消える事故になるため登録時点で弾く。
        /// </summary>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
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

        private void InvokeActionEntries(List<ActionEntry> actions)
        {
            List<ActionEntry> temporaryList = CollectionPool<List<ActionEntry>, ActionEntry>.Get();
            CopyListContents(actions, temporaryList);
            for (int i = 0; i < temporaryList.Count; i++)
            {
                temporaryList[i].Invoke();
                if (temporaryList[i].DisposeOnUsed)
                {
                    actions.Remove(temporaryList[i]);
                }
            }

            CollectionPool<List<ActionEntry>, ActionEntry>.Release(temporaryList);
        }

        /// <summary>
        /// リストの内容を複製する。
        /// </summary>
        /// <param name="baseList">コピー元のリスト</param>
        /// <param name="copy">複製を入れるリスト</param>
        private void CopyListContents(List<ActionEntry> baseList, List<ActionEntry> copy)
        {
            copy.Clear();
            copy.AddRange(baseList);
        }

        /// <summary>
        /// valueは0~1
        /// </summary>
        /// <param name="value"></param>
        public void Report(float value)
        {
            LoadProgress = Mathf.Clamp(0, 1, value);
            if (LoadProgress >= 1)
            {
                IsLoading = false;
            }
        }
    }

    public interface ISceneState : IStateGetter
    {
        public float LoadProgress { get; }
        public bool IsLoading { get; }

        /// <summary> ロード済みの現在のアクティブシーン </summary>
        public int ActiveScene { get; }

        public IReadOnlyList<int> AdditiveScenes { get; }

        public bool IsLoaded(int sceneId);

        /// <summary>
        /// 特定のシーンがロードされたときに実行されるActionを登録する
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="loadedAction">ロードされた際に実行されるAction</param>
        /// <param name="invokeOnAlreadyLoaded">
        /// 登録時にすでにロード済みだった際に実行するかどうか。
        /// 実行しても登録は維持されるかはActionEntryに依存する。
        /// </param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnLoad(int sceneId, ActionEntry loadedAction,
            bool invokeOnAlreadyLoaded = false);

        /// <summary>
        /// 特定のシーンがアンロードされたときに実行されるActionを登録する
        /// </summary>
        /// <param name="sceneId">対象のシーンID</param>
        /// <param name="unloadedAction">アンロードされた際に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventOnUnload(int sceneId, ActionEntry unloadedAction);

        /// <summary>
        /// いずれかのシーンがロードされた際に実行されるActionを登録する
        /// </summary>
        /// <param name="loadedAction">シーンロード時に実行されるAction</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        public IDisposable RegisterEventAnySceneLoaded(ActionEntry loadedAction);
    }
}