using UnityEngine;

namespace Sandbox.Application
{
    /// <summary>
    /// PauseManagerの公開面。GameManagerのような他のApplication層クラスは、
    /// 実装ではなくこのインターフェース経由で参照する。
    /// </summary>
    public interface IPauseManager
    {
        bool IsPaused { get; }
        void SetPause(bool paused);
    }

    /// <summary>
    /// ポーズ状態を持つApplication層のクラス。生成と初期化はPauseInitializerが行い、
    /// 他のInitializerへはIPauseManagerとして配られる。
    /// </summary>
    public sealed class PauseManager : IPauseManager
    {
        public bool IsPaused { get; private set; }

        public void SetPause(bool paused)
        {
            IsPaused = paused;
            Debug.Log($"[CompositerTest] PauseManager.SetPause : {paused}");
        }
    }
}
