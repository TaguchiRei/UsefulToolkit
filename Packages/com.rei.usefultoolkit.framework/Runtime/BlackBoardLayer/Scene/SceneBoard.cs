using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// シーン管理システム専用のChildStateBoard。SceneStateをISceneStateGetterとして保持しつつ、
    /// 実ロード処理の受け渡し口も兼ねる。
    ///
    /// ロードの登録側(EngineServiceLayerのSceneLoadService)と起動側(Applicationのシーン管理クラス)で
    /// APIを分けており、EngineServiceLayerがRegisterSceneLoaderでロードメソッドを預け、
    /// Application側がRequestTransitionAsyncでそれを呼び出す。両者は互いを直接参照しない。
    ///
    /// このボードだけはBlackBoardのコンストラクタで受け取る特別扱いとし、
    /// 「存在するか分からないので毎回TryGetする」という状態を作らない。
    /// </summary>
    public sealed class SceneBoard : ChildStateBoardBase
    {
        private SceneLoadRequest _loader;

        /// <summary>
        /// EngineServiceLayer側: シーン読み込みメソッドを登録する。
        /// </summary>
        /// <returns>Disposeを実行すると登録解除される</returns>
        /// <exception cref="ArgumentNullException">loaderがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">すでにロード処理が登録されているときに出力</exception>
        public IDisposable RegisterSceneLoader(SceneLoadRequest loader)
        {
            if (loader is null) throw new ArgumentNullException(nameof(loader));

            if (_loader != null)
            {
                throw new InvalidOperationException("SceneLoaderはすでに登録されています。二重登録はできません。");
            }

            _loader = loader;
            return new BoardDispose(() => _loader = null);
        }

        /// <summary>
        /// Application側: 指定したシーン集合への遷移をリクエストする。
        /// </summary>
        /// <param name="scenes">遷移後に読み込まれているべきシーン名の一覧。この順番で読み込まれる</param>
        /// <param name="forceReload">trueなら差分計算をせず、管理下のシーンをすべて読み直す</param>
        /// <param name="progress">読み込み進捗の通知先。不要ならnull</param>
        /// <exception cref="ArgumentNullException">scenesがnullのときに出力</exception>
        /// <exception cref="InvalidOperationException">ロード処理が登録されていないときに出力</exception>
        public UniTask RequestTransitionAsync(
            IReadOnlyList<string> scenes, bool forceReload, IProgress<float> progress)
        {
            if (scenes is null) throw new ArgumentNullException(nameof(scenes));

            if (_loader is null)
            {
                throw new InvalidOperationException("SceneLoaderが登録されていません。SceneLoadServiceの初期化が先に必要です。");
            }

            return _loader(scenes, forceReload, progress);
        }
    }
}
