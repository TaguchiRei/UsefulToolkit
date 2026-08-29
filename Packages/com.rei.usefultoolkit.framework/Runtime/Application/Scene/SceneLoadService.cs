using System;
using System.Collections.Generic;
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
        /// 指定したシーングループをロードする。
        /// グループのOverwriteLoadedScenesが立っている場合は、ロード後に
        /// グループへ含まれないアディティブシーンをアンロードする。
        /// インデックスが範囲外の場合と、ISceneStateが未登録の場合はエラーログを出してfalseを返す。
        /// </summary>
        /// <param name="groupIndex">ロードするシーングループのインデックス</param>
        /// <param name="cancellationToken">ロードの中断に使う</param>
        /// <returns>グループの全てのシーンがロードされ、上書き指定時は余剰シーンのアンロードまで終わったか</returns>
        public async UniTask<bool> LoadGroupAsync(int groupIndex, CancellationToken cancellationToken = default)
        {
            if (!TryGetGroup(groupIndex, out var group) || !TryGetSceneState(out var sceneState))
            {
                return false;
            }

            if (!await sceneState.RequestLoadAsync(group.MainSceneId, group.SubSceneIds, cancellationToken))
            {
                return false;
            }

            if (!group.OverwriteLoadedScenes)
            {
                return true;
            }

            return await UnLoadUnusedScenesAsync(sceneState, group, cancellationToken);
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

            var targets = new List<int>(group.SubSceneIds.Count + 1) { group.MainSceneId };
            targets.AddRange(group.SubSceneIds);

            return sceneState.RequestUnLoadAsync(targets, cancellationToken);
        }

        /// <summary>
        /// ロード済みのアディティブシーンのうち、グループへ含まれないものをアンロードする。
        /// </summary>
        /// <param name="sceneState">アンロードを要求する先</param>
        /// <param name="group">残すシーンを決めるシーングループ</param>
        /// <param name="cancellationToken">アンロードの中断に使う</param>
        /// <returns>対象の全てのシーンがアンロードされたか</returns>
        private static UniTask<bool> UnLoadUnusedScenesAsync(ISceneState sceneState, SceneGroup group,
            CancellationToken cancellationToken)
        {
            var additiveScenes = sceneState.AdditiveScenes;

            // アンロード中にアディティブシーンの集合が変化するため、対象を先に複製しておく
            var unusedScenes = new List<int>(additiveScenes.Count);
            for (int i = 0; i < additiveScenes.Count; i++)
            {
                var sceneId = additiveScenes[i];
                if (sceneId == group.MainSceneId || Contains(group.SubSceneIds, sceneId))
                {
                    continue;
                }

                unusedScenes.Add(sceneId);
            }

            if (unusedScenes.Count == 0)
            {
                return UniTask.FromResult(true);
            }

            return sceneState.RequestUnLoadAsync(unusedScenes, cancellationToken);
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

        private static bool Contains(IReadOnlyList<int> sceneIds, int sceneId)
        {
            for (int i = 0; i < sceneIds.Count; i++)
            {
                if (sceneIds[i] == sceneId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
