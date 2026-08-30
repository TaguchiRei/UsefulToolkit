using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// IFuncChainChannelの実装。登録された全ハンドラを数珠つなぎに呼び出し、
    /// 前のハンドラの戻り値を次のハンドラの引数へ渡して値を加工していくイベント経路。
    /// Invokeはこのチャンネルを所有するクラス(EngineServiceLayerや
    /// Applicationのうち、その加工の起点となるクラス)だけが呼ぶこと。
    /// IFuncChainChannel&lt;TPayload&gt;としてしか公開しなければ、外部からInvokeされる事故は型で防げる。
    /// </summary>
    public sealed class FuncChainChannel<TPayload> : IFuncChainChannel<TPayload>
    {
        /// <summary> 優先度の昇順で保持する。同じ優先度同士は登録順を保つ </summary>
        private readonly List<(int Priority, Func<TPayload, TPayload> Handler)> _callbacks = new();

        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        /// <param name="handler">登録するハンドラ</param>
        /// <param name="priority">
        /// 適用順。値が小さいものから先に適用される(InitializationOrderと同じ昇順)。
        /// 同じ優先度同士は登録順に適用される。
        /// </param>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラがすでに登録されているときに出力</exception>
        public IDisposable Register(Func<TPayload, TPayload> handler, int priority = 0)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            // 同じハンドラの二重登録を弾く(デリゲート比較では解除時に2件を区別できない)
            if (_callbacks.Exists(x => x.Handler.Equals(handler)))
            {
                throw new InvalidOperationException($"ハンドラ [{handler.Method.Name}] はすでに登録されています。");
            }

            // 登録時点で優先度順の位置へ挿入する。同じ優先度のグループの末尾へ挿入し、同値同士は登録順を保つ。
            var entry = (Priority: priority, Handler: handler);
            var index = _callbacks.FindIndex(x => x.Priority > priority);

            if (index < 0)
            {
                _callbacks.Add(entry);
            }
            else
            {
                _callbacks.Insert(index, entry);
            }

            return new BoardDispose(() => _callbacks.Remove(entry));
        }

        /// <summary>
        /// 登録されている全ハンドラを優先度順に呼び出し、前のハンドラの戻り値を次のハンドラへ渡す。
        /// 最後のハンドラの戻り値が返り値になる。ハンドラが1つも無い場合はpayloadがそのまま返る。
        /// </summary>
        /// <param name="payload">加工の起点となる値</param>
        public TPayload Invoke(TPayload payload)
        {
            // Invoke中にハンドラ側がRegister/Unregisterしてもこの走査には影響しないようスナップショットする
            var snapshot = _callbacks.ToArray();

            var processedPayload = payload;
            foreach (var (_, handler) in snapshot)
            {
                processedPayload = handler(processedPayload);
            }

            return processedPayload;
        }
    }
}
