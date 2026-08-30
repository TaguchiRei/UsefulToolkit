using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.BlackBoard.Scene
{
    /// <summary>
    /// 実際にシーンをロードする処理。
    /// 渡されたシーンIDを全てロードし、進捗を報告して、成否を返す。
    /// </summary>
    /// <param name="sceneIds">ロードするシーンID。全てまだロードされていないものだけが渡される</param>
    /// <param name="activeSceneId">アクティブシーンにするシーンID。負数ならアクティブシーンを変更しない</param>
    /// <param name="progress">ロード進捗の報告先</param>
    /// <param name="cancellationToken">ロードの中断に使う</param>
    /// <returns>要求した全てのシーンをロードできたか</returns>
    public delegate UniTask<bool> SceneLoadFunc(IReadOnlyList<int> sceneIds, int activeSceneId,
        IProgress<float> progress, CancellationToken cancellationToken);

    /// <summary>
    /// 実際にシーンをアンロードする処理。
    /// 渡されたシーンIDを全てアンロードし、進捗を報告して、成否を返す。
    /// </summary>
    /// <param name="sceneIds">アンロードするシーンID。ロード済みかつアクティブでないものだけが渡される</param>
    /// <param name="progress">アンロード進捗の報告先</param>
    /// <param name="cancellationToken">アンロードの中断に使う</param>
    /// <returns>要求した全てのシーンをアンロードできたか</returns>
    public delegate UniTask<bool> SceneUnLoadFunc(IReadOnlyList<int> sceneIds, IProgress<float> progress,
        CancellationToken cancellationToken);
}
