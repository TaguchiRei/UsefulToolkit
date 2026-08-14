using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// 遷移図の起点になるBootノードの見た目。グラフに必ず1つだけ存在し、削除できない。
    ///
    /// 遷移先を持つ普通のノードとは違い、対応先はSceneNodeData配列ではなく_bootNodeなので、
    /// ノードの追加/削除でインデックスがずれる問題とは無関係。
    /// 出力ポートは1本だけで、つないだ先が起動時の遷移先になる。
    /// </summary>
    internal sealed class SceneFlowBootNodeView : Node
    {
        private readonly SceneFlowGraphSerializer _serializer;

        /// <summary> 起動ノードへ線を引くためのポート。1本しかつなげない </summary>
        public Port OutputPort { get; }

        public SceneFlowBootNodeView(SceneFlowGraphSerializer serializer)
        {
            _serializer = serializer;

            title = "Boot (常駐シーン)";
            style.width = 320f;

            // 消えると起動地点と常駐シーンの指定先がなくなるので、削除とコピーを禁じる。
            // 移動と選択は残しておく。
            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            OutputPort.portName = "起動ノード";
            outputContainer.Add(OutputPort);

            BuildBody();

            RefreshExpandedState();
            RefreshPorts();
            SetPosition(new Rect(serializer.GetBootPosition(), Vector2.zero));
        }

        private void BuildBody()
        {
            var persistentScenes = _serializer.GetPersistentScenesProperty();

            if (persistentScenes != null)
            {
                // シーンenumの型引数を知らなくても、PropertyFieldに任せればドロップダウンまで出せる
                var scenesField = new PropertyField(persistentScenes, "常駐シーン")
                {
                    tooltip = "ロード・アンロードの対象外として、起動時に一度だけ読み込まれ続けるシーン。" +
                              "ForceReloadでも読み直されない"
                };
                extensionContainer.Add(scenesField);
            }

            var groupIndexField = new IntegerField("起動シーングループ") { value = _serializer.GetEntryGroupIndex() };
            groupIndexField.tooltip = "起動ノードのシーングループのうち、起動時に読み込むもののインデックス";
            groupIndexField.RegisterValueChangedCallback(evt => _serializer.SetEntryGroupIndex(evt.newValue));
            extensionContainer.Add(groupIndexField);
        }
    }
}
