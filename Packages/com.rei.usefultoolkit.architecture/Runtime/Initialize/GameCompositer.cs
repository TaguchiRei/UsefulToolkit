using System;
using System.Collections.Generic;
using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.Utility;

namespace UsefulToolkit.Architecture
{
    /// <summary>
    /// シーンの合成ルート。BlackBoardの構築から各Initializerの初期化までを一手に引き受ける。
    ///
    /// ChildBoardの登録・Inject・Initializeの中身はシーンごとに異なるため、この基底では
    /// フックを3つ切るだけに留め、実際の処理はEditor拡張が生成する派生クラスがoverrideする。
    /// 生成側が型を確定させることで、実行時にリフレクションを行わずに済ませている。
    /// </summary>
    [DefaultExecutionOrder(InitializeOrderConst.Compositer)]
    public abstract class GameCompositer : CompositionBase
    {
        private static GameCompositer _instance;

        private readonly Dictionary<Type, object> _container = new();
        private InitializePhase _phase = InitializePhase.None;
        private IBlackBoard _blackBoard;

        private void Awake()
        {
            _instance = this;

            // UsefulToolkit.BlackBoardが名前空間として解決されてしまうため完全修飾する
            _blackBoard = new UsefulToolkit.BlackBoard.BlackBoard.BlackBoard(new SceneBoard());

            _phase = InitializePhase.Collection;

            // BlackBoardも他の依存と同じくInject経路で配る。専用の受け渡しメソッドを生やすより、
            // IInjectable<IBlackBoard>を実装するだけで受け取れる方が扱いが一貫する。
            _container[typeof(IBlackBoard)] = _blackBoard;

            RegisterChildBoards(_blackBoard);

            // この後Unityが各InitializerのAwakeを呼び、その中でTryRegisterContentが実行される。
        }

        private void Start()
        {
            _phase = InitializePhase.Inject;
            InjectAll();

            _phase = InitializePhase.Initialize;
            InitializeAll();
        }

        /// <summary>
        /// このシーンで使うChildBoardをBlackBoardへ登録する。
        /// SceneBoardはBlackBoardのコンストラクタが受け取るため、ここでは登録しない。
        /// </summary>
        protected abstract void RegisterChildBoards(IBlackBoard blackBoard);

        /// <summary>
        /// IInjectableを実装したInitializerへ、収集済みの依存を流し込む。
        /// </summary>
        protected abstract void InjectAll();

        /// <summary>
        /// このシーンの全InitializerのInitializeを呼ぶ。
        /// </summary>
        protected abstract void InitializeAll();

        /// <summary>
        /// 常駐シーンに配置される共通コンポーネントを登録する。Awakeで登録を行う
        /// </summary>
        /// <param name="instance">登録する実体</param>
        /// <typeparam name="T">登録する型</typeparam>
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
