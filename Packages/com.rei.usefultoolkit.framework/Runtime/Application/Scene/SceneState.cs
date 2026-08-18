using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UsefulToolkit.Application.StateManagement;
using UsefulToolkit.Framework.BlackBoard;

namespace UsefulToolkit.Framework.Application
{
    /// <summary>
    /// シーン管理システムのState。現在のシーングループと遷移の進行状況を保持し、
    /// その変化を受け取るAction(RegisterOn〜)と、変化を実行するロード処理
    /// (RegisterSceneLoader)の登録先になる。
    ///
    /// SceneFlowControllerBaseが生成・保持し、ISceneStateGetterとISceneLoaderRegisterの
    /// 2つの型でSceneBoardへ登録する。
    /// </summary>
    public sealed class SceneState : GameStateBase, ISceneLoaderRegister
    {
        private readonly ActionChannel<StateContext<SceneGroupId>> _currentGroupChanged = new();
        private readonly ActionChannel<StateContext<SceneTransitionPhase>> _phaseChanged = new();

        private readonly IReadOnlyList<string> _persistentScenes;

        private SceneLoadRequest _loader;

        private SceneGroupId _currentGroup = SceneGroupId.None;
        private IReadOnlyList<SceneGroupId> _nextGroups = Array.Empty<SceneGroupId>();
        private SceneTransitionPhase _phase = SceneTransitionPhase.Idle;

        public SceneGroupId CurrentGroup => _currentGroup;

        public IReadOnlyList<SceneGroupId> NextGroups => _nextGroups;

        public IReadOnlyList<string> PersistentScenes => _persistentScenes;

        public SceneTransitionPhase Phase => _phase;

        public bool IsTransitioning => _phase == SceneTransitionPhase.Loading;

        /// <exception cref="ArgumentNullException">persistentScenesがnullのときに出力</exception>
        public SceneState(IReadOnlyList<string> persistentScenes)
        {
            _persistentScenes = persistentScenes ?? throw new ArgumentNullException(nameof(persistentScenes));
        }

        public IDisposable RegisterSceneLoader(SceneLoadRequest loader)
        {
            if (loader is null) throw new ArgumentNullException(nameof(loader));

            if (_loader != null)
            {
                throw new InvalidOperationException("SceneLoaderはすでに登録されています。二重登録はできません。");
            }

            _loader = loader;
            return new BoardDispose(() => _loader = null);
        }

        public IDisposable RegisterOnCurrentGroupChanged(Action<StateContext<SceneGroupId>> handler)
        {
            return _currentGroupChanged.Register(handler);
        }

        public IDisposable RegisterOnGroupLoaded(SceneGroupId group, Action action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            void Handler(StateContext<SceneGroupId> context)
            {
                if (context.NewValue == group) action();
            }

            return _currentGroupChanged.Register(Handler);
        }

        public IDisposable RegisterOnPhaseChanged(Action<StateContext<SceneTransitionPhase>> handler)
        {
            return _phaseChanged.Register(handler);
        }

        /// <summary>
        /// 登録されたロード処理を呼び出す。
        /// </summary>
        /// <exception cref="ArgumentNullException">scenesがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">ロード処理が登録されていないときに出力</exception>
        internal UniTask RequestTransitionAsync(
            IReadOnlyList<string> scenes, string activeScene, bool forceReload, IProgress<float> progress)
        {
            if (scenes is null) throw new ArgumentNullException(nameof(scenes));

            if (_loader is null)
            {
                throw new InvalidOperationException(
                    "SceneLoaderが登録されていません。SceneLoadServiceが生成されているか確認してください。");
            }

            return _loader(scenes, activeScene, forceReload, progress);
        }

        /// <summary> PhaseをLoadingにする。CurrentGroupは遷移元のまま据え置く </summary>
        internal void BeginTransition()
        {
            var oldPhase = _phase;
            _phase = SceneTransitionPhase.Loading;

            _phaseChanged.Invoke(new StateContext<SceneTransitionPhase>(oldPhase, _phase));
        }

        /// <summary>
        /// CurrentGroupとNextGroupsを更新し、PhaseをIdleへ戻す。
        /// </summary>
        /// <exception cref="ArgumentNullException">nextGroupsがnullのときに出力</exception>
        internal void CompleteTransition(SceneGroupId group, IReadOnlyList<SceneGroupId> nextGroups)
        {
            if (nextGroups is null) throw new ArgumentNullException(nameof(nextGroups));

            var oldGroup = _currentGroup;
            var oldPhase = _phase;

            // 通知より先にPhaseまで含めて確定させる。Loadingのまま通知すると、
            // 完了通知を受けたハンドラが次の遷移を始められない
            _currentGroup = group;
            _nextGroups = nextGroups;
            _phase = SceneTransitionPhase.Idle;

            Notify(oldPhase, oldGroup);
        }

        /// <summary> CurrentGroupをNone・NextGroupsを空にし、PhaseをFailedにする </summary>
        internal void FailTransition()
        {
            var oldGroup = _currentGroup;
            var oldPhase = _phase;

            _currentGroup = SceneGroupId.None;
            _nextGroups = Array.Empty<SceneGroupId>();
            _phase = SceneTransitionPhase.Failed;

            Notify(oldPhase, oldGroup);
        }

        public override string GetLog()
        {
            return $"Phase: {_phase} / CurrentGroup: {_currentGroup} / " +
                   $"NextGroups: [{string.Join(", ", _nextGroups)}] / " +
                   $"PersistentScenes: [{string.Join(", ", _persistentScenes)}]";
        }

        /// <summary> CurrentGroupは値が変わっていなくても通知する(同じグループへの再遷移も読み直しのため) </summary>
        private void Notify(SceneTransitionPhase oldPhase, SceneGroupId oldGroup)
        {
            _phaseChanged.Invoke(new StateContext<SceneTransitionPhase>(oldPhase, _phase));
            _currentGroupChanged.Invoke(new StateContext<SceneGroupId>(oldGroup, _currentGroup));
        }
    }
}
