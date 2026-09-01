using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.Utility;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// シーンの合成ルートの非ジェネリック基底 (MonoBehaviour)。
    ///
    /// 具体的な初期化ロジックは <see cref="GameCompositor{TSelf}"/> が持つ。この非ジェネリック層は
    /// Editor の TypeCache での列挙点と、Root Compositor が構築する
    /// ゲーム全体で唯一の BlackBoard、および全 Compositor が共有する DI コンテナの置き場として存在する。
    /// </summary>
    public abstract class GameCompositor : MonoBehaviour
    {
        /// <summary>
        /// 全 Compositor が共有する DI コンテナ。中身は Compositor の具象型ごとのスコープに分かれており、
        /// 各 Compositor は自分のスコープと Root スコープしか参照できない。
        /// internal である為、利用側アセンブリの派生クラスからは直接触れない。
        /// </summary>
        internal static readonly CompositionContainer Container = new();

        /// <summary>
        /// <see cref="RootGameCompositor{TSelf}"/> が構築する、ゲーム全体で唯一の BlackBoard。
        /// 非 Root のシーン Compositor はこれを読むだけで、自前では作らない。
        /// </summary>
        protected static IBlackBoard SharedBlackBoard { get; private set; }

        /// <summary>
        /// 共有 BlackBoard を設定する。Root Compositor だけが呼ぶ。
        /// 既に設定済みならエラーログを出して false を返す。
        /// </summary>
        protected static bool TrySetSharedBlackBoard(IBlackBoard blackBoard)
        {
            if (SharedBlackBoard != null)
            {
                UsefulLogger.LogError(
                    "共有 BlackBoard は既に構築されています。Root Compositor を持つシーンが同時に複数存在します。",
                    null);
                return false;
            }

            SharedBlackBoard = blackBoard;
            return true;
        }

        /// <summary>共有 BlackBoard の参照を解放する。Root Compositor の破棄時に呼ぶ。</summary>
        protected static void ClearSharedBlackBoard()
        {
            SharedBlackBoard = null;
        }
    }

    /// <summary>
    /// シーンごとの合成ルート。BlackBoard 上の各 Initializer への Inject / Initialize を受け持つ。
    ///
    /// CRTP (<typeparamref name="TSelf"/> に自分自身を渡す) により、静的メンバである
    /// <c>_instance</c> を派生具象型ごとに分離し、同時に DI コンテナのスコープキーとしても使う。
    /// これにより、同時に生きている Compositor が複数あっても取り合いが起きない。
    /// 依存の解決範囲は自分のスコープと Root スコープの 2 つで、常駐シーンで生成された実体は
    /// どのシーンからでも受け取れるが、シーン同士がお互いのスコープを覗くことはできない。
    /// 逆に、同一の具象 Compositor 型のインスタンスを同時に複数生かすことは想定しない
    /// (別シーンで同じ機能が要るならシーンごと分ける、というのが本設計の前提)。
    ///
    /// Inject / Initialize の中身はシーンごとに異なるため、この基底ではフックを 2 つ切るだけに留め、
    /// 実際の処理は Editor 拡張が生成する派生クラスが override する。
    /// ChildBoard の登録と共有 BlackBoard の構築は <see cref="RootGameCompositor{TSelf}"/> の担当。
    /// </summary>
    [DefaultExecutionOrder(InitializeOrderConst.Compositor)]
    public abstract class GameCompositor<TSelf> : GameCompositor
        where TSelf : GameCompositor<TSelf>
    {
        private static TSelf _instance;

        private InitializePhase _phase = InitializePhase.None;

        protected virtual void Awake()
        {
            if (_instance != null)
            {
                UsefulLogger.LogError(
                    $"{typeof(TSelf).Name} が既にシーンに存在します。" +
                    "同一の Compositor 型を同時に複数生かすことはできません。", this);
                enabled = false;
                return;
            }

            _instance = (TSelf)this;

            if (SharedBlackBoard == null)
            {
                UsefulLogger.LogError(
                    $"{typeof(TSelf).Name} : 共有 BlackBoard が構築されていません。" +
                    "Root Compositor を持つ常駐シーンを先に読み込んでください。", this);
                enabled = false;
                return;
            }

            _phase = InitializePhase.Collection;

            // この後 Unity が各 InitializerBase の Awake を呼び、その中で TryRegisterContent が走る。
        }

        protected virtual void Start()
        {
            // Awake で停止している場合は何もしない。
            if (_phase != InitializePhase.Collection) return;

            _phase = InitializePhase.Inject;
            InjectAll();

            _phase = InitializePhase.Initialize;
            InitializeAll(SharedBlackBoard);
        }

        protected virtual void OnDestroy()
        {
            // Domain Reload 無効時に静的参照が残らないよう、自分が入れた分だけ片付ける。
            if (_instance == this)
            {
                _instance = null;
                Container.ClearScope(typeof(TSelf));
            }
        }

        /// <summary>
        /// この Compositor 型のスコープを Root スコープにする。常駐シーンの Compositor だけが呼ぶ。
        /// 既に別の型が Root スコープなら false を返す。
        /// </summary>
        private protected static bool TrySetAsRootScope()
        {
            return Container.TrySetRootScope(typeof(TSelf));
        }

        /// <summary>Root スコープの指定を解除する。設定した Compositor の破棄時に呼ぶ。</summary>
        private protected static void ClearRootScope()
        {
            Container.ClearRootScope(typeof(TSelf));
        }

        /// <summary>
        /// 誤用を検出した際に、このシーンの初期化を打ち切る。
        /// フェーズを None に戻す事で Start での Inject / Initialize が走らなくなる。
        /// </summary>
        private void AbortInitialize()
        {
            _phase = InitializePhase.None;
            enabled = false;
        }

        /// <summary>
        /// IInjectable を実装した Initializer へ、収集済みの依存を流し込む。
        /// 配るのは各 Initializer が「自分の初期化対象へ渡すために必要とする」
        /// クラス / インターフェースであって、Initializer そのものではない。
        /// </summary>
        protected abstract void InjectAll();

        /// <summary>このシーンの全 Initializer の Initialize を呼ぶ。</summary>
        /// <param name="blackBoard">各 Initializer へ渡す共有 BlackBoard</param>
        protected abstract void InitializeAll(IBlackBoard blackBoard);

        /// <summary>
        /// 他の Initializer が初期化に必要とするクラス / インターフェースを登録する。
        /// 各 Initializer の Awake から、自分のシーンの具象 Compositor 型を指定して呼ぶ。
        /// (例: <c>InGameCompositor.TryRegisterContent&lt;IPauseManager&gt;(pauseManager)</c>)
        /// 公開したい面だけを渡せるよう、T には実装クラスではなくインターフェースを指定するのが基本。
        ///
        /// 自分のスコープ、または Root スコープに同じ型が既に登録されている場合はエラーログを出し、
        /// このシーンの初期化を中断する。
        /// </summary>
        /// <param name="instance">登録する実体</param>
        /// <typeparam name="T">登録する型。この型が Inject 時の解決キーになる</typeparam>
        /// <returns>登録が成功したら true</returns>
        public static bool TryRegisterContent<T>(T instance)
        {
            if (_instance == null)
            {
                UsefulLogger.LogError($"{typeof(TSelf).Name} がシーンに存在しません : {typeof(T).Name}", null);
                return false;
            }

            if (_instance._phase != InitializePhase.Collection)
            {
                UsefulLogger.LogError(
                    $"現在は収集フェーズではありません。Awake で登録してください。現在のフェーズ {_instance._phase}",
                    _instance);
                return false;
            }

            var result = Container.TryAdd(typeof(TSelf), typeof(T), instance);

            if (result != CompositionContainer.AddResult.Success)
            {
                string reason = result == CompositionContainer.AddResult.DuplicateInRootScope
                    ? "常駐シーンの Compositor が既に同じ型を登録しています"
                    : "この Compositor が既に同じ型を登録しています";

                UsefulLogger.LogError(
                    $"依存の登録が重複しています : {typeof(T).Name} ({reason})。" +
                    "同じ型の実体が複数の経路で配られると参照が分裂する為、このシーンの初期化を中断します。",
                    _instance);

                _instance.AbortInitialize();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 登録済みの依存を取得する。生成された派生クラスの InjectAll から呼ばれる。
        /// 自分のスコープを先に探し、見つからなければ Root スコープを探す。
        /// </summary>
        /// <param name="instance">取得した実体</param>
        /// <typeparam name="T">取得する型</typeparam>
        /// <returns>取得できたら true</returns>
        protected static bool TryGetContent<T>(out T instance)
        {
            return Container.TryGet<T>(typeof(TSelf), out instance);
        }
    }
}
