using UnityEngine;

namespace Sandbox.Application
{
    /// <summary>
    /// 同じApplication層のIPauseManagerを必要とするクラス。
    /// 自分では依存を探さず、GameManagementInitializerから渡してもらう。
    /// </summary>
    public sealed class GameManager
    {
        private readonly IPauseManager _pauseManager;

        public GameManager(IPauseManager pauseManager)
        {
            _pauseManager = pauseManager;
        }

        public void Boot()
        {
            Debug.Log($"[CompositerTest] GameManager.Boot pauseManager={_pauseManager != null}");

            // 受け取った参照が本当に使えるかまで確認する
            _pauseManager.SetPause(true);
            Debug.Log($"[CompositerTest] GameManager から見たIsPaused : {_pauseManager.IsPaused}");
        }
    }
}
