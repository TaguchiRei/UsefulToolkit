using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.Utility;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// シーンの合成ルート。BlackBoardの構築から各Initializerの初期化までを一手に引き受ける。
    ///
    /// ChildBoardの登録・Inject・Initializeの中身はシーンごとに異なるため、この基底では
    /// フックを3つ切るだけに留め、実際の処理はEditor拡張が生成する派生クラスがoverrideする。
    /// 生成側が型を確定させることで、実行時にリフレクションを行わずに済ませている。
    ///
    /// UsefulToolkitRuntimeInitializerだけは、他のInitializerより先に動かす必要があるため
    /// このクラスがAwakeで直接Initializeを呼ぶ。生成される派生クラスの初期化対象には含まれない。
    /// </summary>
    [DefaultExecutionOrder(InitializeOrderConst.Compositer)]
    public abstract class GameCompositer : CompositionBase
    {
        private static GameCompositer _instance;

        /// <summary>
        /// Toolkitのランタイム機能を初期化するInitializer。
        /// 他のInitializerのAwakeより先に初期化するため、Compositerが直接参照して呼ぶ。
        /// </summary>
        [SerializeField] private UsefulToolkitRuntimeInitializer _runtimeInitializer;

        private readonly Dictionary<Type, object> _container = new();
        private InitializePhase _phase = InitializePhase.None;
        private IBlackBoard _blackBoard;

        private void Awake()
        {
            _instance = this;

            // UsefulToolkit.BlackBoardが名前空間として解決されてしまうため完全修飾する
            _blackBoard = new UsefulToolkit.BlackBoard.BlackBoard.BlackBoard(new SceneBoard());

            // 他のInitializerのAwakeから既にシーンシステムを使えるよう、ここで真っ先に初期化する。
            if (_runtimeInitializer != null)
            {
                _runtimeInitializer.Initialize(_blackBoard);
            }
            else
            {
                UsefulLogger.LogError("UsefulToolkitRuntimeInitializerが設定されていない為、シーンシステムは初期化されません。", this);
            }

            _phase = InitializePhase.Collection;

            // BlackBoardはコンテナへ入れない。コンテナが預かるのは、あるInitializerが生成し
            // 他のInitializerが初期化に必要とするクラス/インターフェースであって、
            // Stateへの参照を公開するBlackBoardとは役割が違う。
            // BlackBoardはInitializeの引数として全Initializerへ直接渡す。
            RegisterChildBoards(_blackBoard);

            // この後Unityが各InitializerのAwakeを呼び、その中でTryRegisterContentが実行される。
        }

        private void Start()
        {
            _phase = InitializePhase.Inject;
            InjectAll();

            _phase = InitializePhase.Initialize;
            InitializeAll(_blackBoard);
        }

        /// <summary>
        /// このシーンで使うChildBoardをBlackBoardへ登録する。
        /// SceneBoardはBlackBoardのコンストラクタが受け取るため、ここでは登録しない。
        /// </summary>
        protected abstract void RegisterChildBoards(IBlackBoard blackBoard);

        /// <summary>
        /// IInjectableを実装したInitializerへ、収集済みの依存を流し込む。
        /// 配るのは各Initializerが「自分の初期化対象へ渡すために必要とする」
        /// クラス/インターフェースであって、Initializerそのものではない。
        /// </summary>
        protected abstract void InjectAll();

        /// <summary>
        /// このシーンの全InitializerのInitializeを呼ぶ。
        /// </summary>
        /// <param name="blackBoard">各Initializerへ渡すBlackBoard</param>
        protected abstract void InitializeAll(IBlackBoard blackBoard);

        /// <summary>
        /// 他のInitializerが初期化に必要とするクラス/インターフェースを登録する。
        /// 各InitializerのAwakeから、自分が生成した実体を登録すること。
        /// 公開したい面だけを渡せるよう、Tには実装クラスではなくインターフェースを
        /// 明示的に指定するのが基本。(例: TryRegisterContent&lt;IPauseManager&gt;(pauseManager))
        /// </summary>
        /// <param name="instance">登録する実体</param>
        /// <typeparam name="T">登録する型。この型がInject時の解決キーになる</typeparam>
        /// <returns>登録が成功したらtrue</returns>
        public static bool TryRegisterContent<T>(T instance)
        {
            if (_instance == null)
            {
                UsefulLogger.LogError($"GameCompositerがシーンに存在しません : {typeof(T).Name}", null);
                return false;
            }

            if (_instance._phase != InitializePhase.Collection)
            {
                UsefulLogger.LogError(
                    $"現在は収集フェーズではありません。Awakeで登録してください。現在のフェーズ{_instance._phase.ToString()}",
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

        /// <summary>
        /// 登録済みの依存を取得する。生成された派生クラスのInjectAllから呼ばれる。
        /// </summary>
        /// <param name="instance">取得した実体</param>
        /// <typeparam name="T">取得する型</typeparam>
        /// <returns>取得できたらtrue</returns>
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
