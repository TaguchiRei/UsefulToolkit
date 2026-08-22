using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// IFuncCollectChannelの実装。登録された全ハンドラへ同じ引数を渡して呼び出し、
    /// それぞれの戻り値をまとめて受け取る「全員に問い合わせる」イベント経路。
    /// Invokeはこのチャンネルを所有するクラス(EngineServiceLayerや
    /// Applicationのうち、その問い合わせの発生源となるクラス)だけが呼ぶこと。
    /// IFuncCollectChannel&lt;TArgument, TReturnValue&gt;としてしか公開しなければ、
    /// 外部からInvokeされる事故は型で防げる。
    /// </summary>
    public sealed class FuncCollectChannel<TArgument, TReturnValue> : IFuncCollectChannel<TArgument, TReturnValue>
    {
        private readonly List<Func<TArgument, TReturnValue>> _callbacks = new();

        /// <summary>
        /// ハンドラを登録する。返り値のIDisposableをDisposeすることで解除する。
        /// </summary>
        /// <param name="handler">登録するハンドラ</param>
        /// <exception cref="ArgumentNullException">handlerがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">同じハンドラがすでに登録されているときに出力</exception>
        public IDisposable Register(Func<TArgument, TReturnValue> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            // 同じハンドラを2回登録すると、デリゲート比較による解除がどちらか一方しか区別できず、
            // 片方をDisposeしたときにもう片方が消える事故になるため登録時点で弾く
            if (_callbacks.Contains(handler))
            {
                throw new InvalidOperationException($"ハンドラ [{handler.Method.Name}] はすでに登録されています。");
            }

            _callbacks.Add(handler);
            return new BoardDispose(() => _callbacks.Remove(handler));
        }

        /// <summary>
        /// 登録されている全ハンドラを登録順に呼び出し、戻り値を登録順の配列で返す。
        /// どの要素がどのハンドラの結果かは順序でしか表現されないため、判別が必要な場合は
        /// TReturnValue側に識別子を含めること。ハンドラが1つも無い場合は空配列を返す。
        /// </summary>
        /// <param name="argument">全ハンドラへ渡す引数</param>
        public TReturnValue[] Invoke(TArgument argument)
        {
            // Invoke中にハンドラ側がRegister/Unregisterしてもこの走査には影響しないようスナップショットする。
            // 走査中の増減で戻り値配列の長さが変わらないようにする意味もある
            var snapshot = _callbacks.ToArray();

            if (snapshot.Length == 0) return Array.Empty<TReturnValue>();

            var result = new TReturnValue[snapshot.Length];
            for (var i = 0; i < snapshot.Length; i++)
            {
                result[i] = snapshot[i](argument);
            }

            return result;
        }
    }
}
