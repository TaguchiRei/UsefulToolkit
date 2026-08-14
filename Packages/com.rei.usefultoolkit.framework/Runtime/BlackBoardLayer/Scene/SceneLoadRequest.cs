using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace UsefulToolkit.Framework.BlackBoard
{
    /// <summary>
    /// SceneBoardを挟んで受け渡しされるシーン読み込み処理の型。
    /// EngineServiceLayer側が実装を登録し、Application側が呼び出す。
    /// forceReloadの判断材料(SceneGroup)はExternalLayerにあるが、
    /// BlackBoardLayerはExternalLayerを参照できないため、boolまで落として受け取る。
    /// </summary>
    /// <param name="scenes">遷移後に読み込まれているべきシーン名の一覧。この順番で読み込む</param>
    /// <param name="forceReload">trueなら差分計算をせず、管理下のシーンをすべて読み直す</param>
    /// <param name="progress">読み込み進捗(0..1)の通知先。不要ならnull</param>
    public delegate UniTask SceneLoadRequest(
        IReadOnlyList<string> scenes,
        bool forceReload,
        IProgress<float> progress);
}
