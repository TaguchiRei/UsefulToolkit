using System;
using System.Collections.Generic;
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
    /// ゲーム全体で唯一の BlackBoard の共有点としてだけ存在する。
    /// </summary>
    public abstract class GameCompositor : MonoBehaviour
    {
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
    /// <c>_instance</c> と DI コンテナを派生具象型ごとに分離する。これにより、
    /// 同時に生きている Compositor が複数あっても取り合いが起きない。
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

        private readonly Dictionary<Type, object> _container = new();
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
            }
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

            if (!_instance._container.TryAdd(typeof(T), instance))
            {
                UsefulLogger.LogWarning($"この型は登録済みです : {typeof(T).Name}", _instance);
                return false;
            }

            return true;
        }

        /// <summary>登録済みの依存を取得する。生成された派生クラスの InjectAll から呼ばれる。</summary>
        /// <param name="instance">取得した実体</param>
        /// <typeparam name="T">取得する型</typeparam>
        /// <returns>取得できたら true</returns>
        protected static bool TryGetContent<T>(out T instance)
        {
            if (_instance != null && _instance._container.TryGetValue(typeof(T), out var raw) && raw is T typed)
            {
                instance = typed;
                return true;
            }

            instance = default;
            return false;
        }
    }
}
