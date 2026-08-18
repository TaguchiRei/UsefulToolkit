using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// シーン読み込み処理の型。EngineServiceLayerが実装を登録し、Applicationが呼び出す。
    /// </summary>
    /// <param name="scenes">遷移後に読み込まれているべきシーン名の一覧。この順番で読み込む</param>
    /// <param name="activeScene">読み込み後にアクティブシーンにするシーン名。不要なら空文字</param>
    /// <param name="forceReload">trueなら差分計算をせず、管理下のシーンをすべて読み直す</param>
    /// <param name="progress">読み込み進捗(0..1)の通知先。不要ならnull</param>
    public delegate UniTask SceneLoadRequest(
        IReadOnlyList<string> scenes,
        string activeScene,
        bool forceReload,
        IProgress<float> progress);
}
