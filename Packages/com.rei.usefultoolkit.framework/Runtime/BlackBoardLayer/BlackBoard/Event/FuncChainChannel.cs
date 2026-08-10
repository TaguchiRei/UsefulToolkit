using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// IFuncChainChannelの実装。登録された全ハンドラを数珠つなぎに呼び出し、
    /// 前のハンドラの戻り値を次のハンドラの引数へ渡して値を加工していくイベント経路。
    /// Publishはこのチャンネルを所有するクラス(EngineServiceLayerや
    /// Applicationのうち、その加工の起点となるクラス)だけが呼ぶこと。
    /// IFuncChainChannel&lt;TPayload&gt;としてしか公開しなければ、外部からPublishされる事故は型で防げる。
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

            // 同じハンドラを2回登録すると、デリゲート比較による解除がどちらか一方しか区別できず、
            // 片方をDisposeしたときにもう片方が消える事故になるため登録時点で弾く
            if (_callbacks.Exists(x => x.Handler.Equals(handler)))
            {
                throw new InvalidOperationException($"ハンドラ [{handler.Method.Name}] はすでに登録されています。");
            }

            // Publishのたびに並べ替えずに済むよう、登録時点で優先度順の位置へ挿入する。
            // 同じ優先度のグループの末尾へ挿入することで同値同士の登録順が保たれる
            // (List.Sortは安定ソートではないため、Publish時のソートでは順序を保証できない)
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
            // Publish中にハンドラ側がRegister/Unregisterしてもこの走査には影響しないようスナップショットする
            var snapshot = _callbacks.ToArray();

            var payloadToPublish = payload;
            foreach (var (_, handler) in snapshot)
            {
                payloadToPublish = handler(payloadToPublish);
            }

            return payloadToPublish;
        }
    }
}
