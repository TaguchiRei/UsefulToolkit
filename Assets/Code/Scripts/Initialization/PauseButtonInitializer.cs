using Sandbox.Application;
using Sandbox.BlackBoard;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.ProgramTools;
using UsefulToolkit.Initialization;

namespace Sandbox.Initialization
{
    /// <summary>
    /// 各シーンに置く消費側の Initializer。常駐シーンで生成された IPauseManager を
    /// Inject で受け取り、OnGUI のボタンから操作する。
    ///
    /// このクラスはインゲーム / アウトゲームのどちらにも同じものを置ける。
    /// ポーズ機能側はどのシーンのボタンが呼んでいるかを一切知らない。
    /// </summary>
    public sealed class PauseButtonInitializer : InitializerBase, IInjectable<IPauseManager>
    {
        [SerializeField]
        [Tooltip("画面表示に使うラベル。どのシーンのボタンかを見分ける為に使う。")]
        private string _label = "Scene";

        [SerializeField]
        [Tooltip("OnGUI の表示位置。複数シーンを同時に出しても重ならないようにする。")]
        private Vector2 _guiPosition = new Vector2(10f, 320f);

        private IPauseManager _pauseManager;
        private IPauseState _pauseState;

        public void Inject(IPauseManager instance)
        {
            _pauseManager = instance;
        }

        public override void Initialize(IBlackBoard blackBoard)
        {
            base.Initialize(blackBoard);

            if (blackBoard.TryGetStateBoard<PauseBoard>(out var pauseBoard))
            {
                pauseBoard.TryGetGameState<IPauseState>(out _pauseState);
            }
        }

        private void OnGUI()
        {
            if (!Initialized) return;

            var area = new Rect(_guiPosition.x, _guiPosition.y, 320f, 130f);

            using (new GUILayout.AreaScope(area, GUIContent.none, GUI.skin.box))
            {
                GUILayout.Label($"[{_label}] PauseButtonInitializer");
                GUILayout.Label($"IPauseManager : {(_pauseManager == null ? "未注入" : "注入済み")}");
                GUILayout.Label($"IPauseState : {(_pauseState == null ? "未取得" : _pauseState.IsPaused ? "Paused" : "Running")}");

                if (_pauseManager == null) return;

                if (GUILayout.Button("Toggle Pause"))
                {
                    _pauseManager.Toggle();
                }
            }
        }
    }
}
