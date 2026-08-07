using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard
{
    /// <summary>
    /// IEventChannelの実装。Publishはこのチャンネルを所有するクラス(EngineServiceLayerや
    /// Applicationのうち、そのイベントの発生源となるクラス)だけが呼ぶこと。
    /// IEventChannel&lt;TPayload&gt;としてしか公開しなければ、外部からPublishされる事故は型で防げる。
    /// </summary>
    public sealed class EventChannel<TPayload> : IEventChannel<TPayload>
    {
        private readonly List<Action<TPayload>> _handlers = new();

        public IDisposable Register(Action<TPayload> handler)
        {
            _handlers.Add(handler);
            return new StateDispose(() => _handlers.Remove(handler));
        }

        public void Publish(TPayload payload)
        {
            // Publish中にハンドラ側がRegister/Unregisterしてもこの走査には影響しないようスナップショットする
            var snapshot = _handlers.ToArray();
            foreach (var handler in snapshot)
            {
                handler.Invoke(payload);
            }
        }
    }
}
