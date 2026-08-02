using System;
using System.Collections.Generic;
using UsefulToolkit.Application.StateManagement;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 現在開いているシーンをApplication/EngineServiceLayerへ公開するGetterインターフェース。
    /// アクティブシーンと、Additiveでロード中の全シーン名を持つ。
    /// </summary>
    public interface ISceneStateGetter : IStateGetter
    {
        string ActiveSceneName { get; }
        event Action<StateContext<string>> OnActiveSceneChanged;

        IReadOnlyList<string> AdditiveSceneNames { get; }
        event Action<StateContext<IReadOnlyList<string>>> OnAdditiveScenesChanged;
    }

    /// <summary>
    /// SceneServiceが唯一の書き手となるState(Single Writer)。シーン遷移そのものを追跡する
    /// 役割のため、追跡対象のシーンがUnloadされても消えては困る——GameStateBaseとして
    /// ゲーム終了まで生存させる。
    /// </summary>
    public sealed class SceneState : GameStateBase, ISceneStateGetter
    {
        public string ActiveSceneName { get; private set; } = string.Empty;
        public event Action<StateContext<string>> OnActiveSceneChanged;

        public IReadOnlyList<string> AdditiveSceneNames { get; private set; } = Array.Empty<string>();
        public event Action<StateContext<IReadOnlyList<string>>> OnAdditiveScenesChanged;

        internal void SetActiveScene(string sceneName)
        {
            if (ActiveSceneName == sceneName) return;

            var old = ActiveSceneName;
            ActiveSceneName = sceneName;
            OnActiveSceneChanged?.Invoke(new StateContext<string>(old, sceneName));
        }

        internal void SetAdditiveScenes(IReadOnlyList<string> sceneNames)
        {
            var old = AdditiveSceneNames;
            AdditiveSceneNames = sceneNames;
            OnAdditiveScenesChanged?.Invoke(new StateContext<IReadOnlyList<string>>(old, sceneNames));
        }
    }
}
