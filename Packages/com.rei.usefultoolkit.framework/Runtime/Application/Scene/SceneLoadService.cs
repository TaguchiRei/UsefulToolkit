using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UsefulToolkit.BlackBoard.BlackBoard;
using UsefulToolkit.BlackBoard.Logger;
using UsefulToolkit.BlackBoard.Scene;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Application.Scene
{
    /// <summary>
    /// 保持しているシーングループの単位で、シーンのロード/アンロードを要求する。
    /// どのグループを操作するかは、コンストラクタへ渡した配列のインデックスで指定する。
    /// </summary>
    public sealed class SceneLoadService
    {
        private readonly SceneBoard _sceneBoard;
        private readonly SceneGroup[] _sceneGroups;

        private bool _initialized;

        /// <summary> 操作できるシーングループの数 </summary>
        public int GroupCount => _sceneGroups.Length;

        /// <param name="blackBoard">ISceneStateの取得元</param>
        /// <param name="sceneGroups">操作対象のシーングループ。渡した配列は複製されて保持される</param>
        /// <exception cref="ArgumentNullException">blackBoardまたはsceneGroupsがnullのときに出力</exception>
        public SceneLoadService(IBlackBoard blackBoard, SceneGroup[] sceneGroups)
        {
            if (blackBoard == null)
            {
                throw new ArgumentNullException(nameof(blackBoard));
            }

            if (sceneGroups == null)
            {
                throw new ArgumentNullException(nameof(sceneGroups));
            }

            _sceneBoard = blackBoard.GetSceneBoard();
            _sceneGroups = (SceneGroup[])sceneGroups.Clone();
        }

        /// <summary>
        /// 常駐シーンだけがロードされた起動直後の状態から、開始シーングループへ遷移する。
        /// UsefulToolkitの常駐シーンから起動すると常駐シーンが最初のアクティブシーンになり、
        /// このメソッドを呼ぶことで <paramref name="startGroupIndex"/> のグループのメインシーンが
        /// 本来のアクティブシーンになる。呼べるのは一度だけ。
        ///
        /// メインシーンを持たないグループを開始グループに指定した場合、アクティブシーンは
        /// 常駐シーンのまま残る。
        /// </summary>
        /// <param name="startGroupIndex">起動時にロードするシーングループのインデックス。既定は0</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>開始グループのロードが完了したか。二度目以降の呼び出しはfalse</returns>
        public UniTask<bool> Initialize(int startGroupIndex = 0, CancellationToken cancellationToken = default)
        {
            if (_initialized)
            {
                UsefulLogger.LogWarning("SceneLoadServiceは既に初期化済みです。", this);
                return UniTask.FromResult(false);
            }

            _initialized = true;

            // 上書きロードで開始グループへ遷移する。
            return LoadGroupAsync(startGroupIndex, overwriteLoadedScenes: true, cancellationToken);
        }

        /// <summary>
        /// 指定したシーングループをロードする。
        /// インデックスが範囲外の場合と、ISceneStateが未登録の場合はエラーログを出してfalseを返す。
        /// </summary>
        /// <param name="groupIndex">ロードするシーングループのインデックス</param>
        /// <param name="overwriteLoadedScenes">
        /// trueなら、グループへ含まれず常駐でもないロード済みシーン(アクティブシーンを含む)を
        /// 全てアンロードしてから、このグループをロードする(元のシーン状況を丸ごと上書き)。
        /// falseなら追加でロードするだけで、既存のロード済みシーンはそのまま残る。
        /// </param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>グループの全てのシーンがロードされ、上書き指定時は余剰シーンのアンロードまで終わったか</returns>
        public UniTask<bool> LoadGroupAsync(int groupIndex, bool overwriteLoadedScenes,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetGroup(groupIndex, out var group) || !TryGetSceneState(out var sceneState))
            {
                return UniTask.FromResult(false);
            }

            var mainSceneId = group.TryGetMainSceneId(out var id) ? id : SceneState.NoSceneId;

            // overwrite指定時は「元のシーン状況を丸ごとこのグループで上書き」する。
            // 非指定時は追加でロードするだけ。
            return overwriteLoadedScenes
                ? sceneState.RequestOverwriteLoadAsync(mainSceneId, group.AdditiveSceneIds, cancellationToken)
                : sceneState.RequestLoadAsync(mainSceneId, group.AdditiveSceneIds, cancellationToken);
        }

        /// <summary>
        /// 指定したシーングループのシーンをアンロードする。
        /// ロードされていないシーンとアクティブシーンは対象から外れる。
        /// インデックスが範囲外の場合と、ISceneStateが未登録の場合はエラーログを出してfalseを返す。
        /// </summary>
        /// <param name="groupIndex">アンロードするシーングループのインデックス</param>
        /// <param name="cancellationToken">アンロードの中断に使う</param>
        /// <returns>対象の全てのシーンがアンロードされたか</returns>
        public UniTask<bool> UnLoadGroupAsync(int groupIndex, CancellationToken cancellationToken = default)
        {
            if (!TryGetGroup(groupIndex, out var group) || !TryGetSceneState(out var sceneState))
            {
                return UniTask.FromResult(false);
            }

            // グループの全シーン(メイン含む)を対象にする。
            // アクティブシーンと未ロードのシーンはRequestUnLoadAsync側で除外される。
            return sceneState.RequestUnLoadAsync(group.SceneIds, cancellationToken);
        }

        /// <summary>
        /// インデックスからシーングループを取得する。範囲外の場合はエラーログを出す。
        /// </summary>
        /// <param name="groupIndex">シーングループのインデックス</param>
        /// <param name="group">取得したシーングループ</param>
        /// <returns>取得できたか</returns>
        private bool TryGetGroup(int groupIndex, out SceneGroup group)
        {
            if (groupIndex < 0 || groupIndex >= _sceneGroups.Length)
            {
                UsefulLogger.LogError($"シーングループのインデックス{groupIndex}は範囲外です。", this);
                group = null;
                return false;
            }

            group = _sceneGroups[groupIndex];
            return true;
        }

        /// <summary>
        /// SceneBoardからISceneStateを取得する。未登録の場合はエラーログを出す。
        /// </summary>
        /// <param name="sceneState">取得したISceneState</param>
        /// <returns>取得できたか</returns>
        private bool TryGetSceneState(out ISceneState sceneState)
        {
            if (_sceneBoard.TryGetGameState(out sceneState))
            {
                return true;
            }

            UsefulLogger.LogError("ISceneStateが登録されていない為、シーングループを操作できません。", this);
            return false;
        }
    }
}
