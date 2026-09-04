using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// IActionChannelの実装。戻り値を持たない「通知して終わり」のイベント経路。
    /// Invokeはこのチャンネルを所有するクラス(EngineAdapterLayerや
    /// Applicationのうち、そのイベントの発生源となるクラス)だけが呼ぶこと。
    /// IActionChannel&lt;TPayload&gt;としてしか公開しなければ、外部からInvokeされる事故は型で防げる。
    /// </summary>
    public sealed class ActionChannel<TPayload> : IActionChannel<TPayload>
    {
        private readonly List<Action<TPayload>> _handlers = new();

        /// <summary> Invokeで走査する配列。登録内容が変わるまで使い回す </summary>
        private Action<TPayload>[] _snapshot = Array.Empty<Action<TPayload>>();

        /// <summary> 登録内容が変わり、スナップショットの作り直しが必要か </summary>
        private bool _snapshotDirty;

        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        /// <param name="handler">登録するハンドラ</param>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラがすでに登録されているときに出力</exception>
        public IDisposable Register(Action<TPayload> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            // 同じハンドラを2回登録すると、デリゲート比較による解除がどちらか一方しか区別できず、
            // 片方をDisposeしたときにもう片方が消える事故になるため登録時点で弾く
            if (_handlers.Contains(handler))
            {
                throw new InvalidOperationException($"ハンドラ [{handler.Method.Name}] はすでに登録されています。");
            }

            _handlers.Add(handler);
            _snapshotDirty = true;

            return new BoardDispose(() =>
            {
                if (_handlers.Remove(handler)) _snapshotDirty = true;
            });
        }

        /// <summary>
        /// 登録されている全ハンドラを登録順に呼び出す。
        /// </summary>
        /// <param name="payload">ハンドラへ渡す値</param>
        public void Invoke(TPayload payload)
        {
            if (_snapshotDirty)
            {
                _snapshot = _handlers.ToArray();
                _snapshotDirty = false;
            }

            // 走査中にハンドラ側がRegister/Unregisterしても、作り直されるのは次回のInvokeからになる。
            // ローカルへ退避しておくことで、入れ子のInvokeが作り直してもこの回の走査対象は変わらない
            var snapshot = _snapshot;
            foreach (var handler in snapshot)
            {
                handler.Invoke(payload);
            }
        }
    }
}
