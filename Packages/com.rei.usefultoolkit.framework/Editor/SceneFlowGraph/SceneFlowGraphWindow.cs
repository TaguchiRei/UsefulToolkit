using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UsefulToolkit.Framework.External;

namespace UsefulToolkit.Framework
{
    /// <summary>
    /// シーン遷移図をノードグラフとして編集するウィンドウ。
    /// SceneFlowアセットをダブルクリックしても開く。
    ///
    /// 編集内容はSerializedObject経由でその場でアセットへ書き込まれるので、保存ボタンはない。
    /// Undo/Redoにもそのまま乗る。
    /// </summary>
    public sealed class SceneFlowGraphWindow : EditorWindow
    {
        [SerializeField] private SceneFlowAssetBase _asset;

        private SceneFlowGraphView _graphView;
        private SceneFlowGraphSerializer _serializer;
        private ObjectField _assetField;
        private ScrollView _messageArea;

        [MenuItem("UsefulToolkit/Scene Flow Graph")]
        public static void Open() => Open(null);

        public static void Open(SceneFlowAssetBase asset)
        {
            var window = GetWindow<SceneFlowGraphWindow>();
            window.titleContent = new GUIContent("Scene Flow Graph");

            if (asset != null) window.SetAsset(asset);

            window.Show();
        }

        /// <summary>
        /// SceneFlowアセットのダブルクリックでインスペクタではなくこのウィンドウを開く。
        ///
        /// instanceIdからオブジェクトを引くint版のAPIはUnity 6000.3で非推奨になったが、
        /// 代替のEntityId版はpackage.jsonの対応下限(6000.0)には存在しない。
        /// 両対応のためint版を使い、警告だけを抑えている。
        /// </summary>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618
            var opened = EditorUtility.InstanceIDToObject(instanceId);
#pragma warning restore CS0618

            if (opened is not SceneFlowAssetBase asset) return false;

            Open(asset);
            return true;
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedoPerformed;

        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedoPerformed;

        private void CreateGUI()
        {
            var toolbar = new Toolbar();

            _assetField = new ObjectField
            {
                objectType = typeof(SceneFlowAssetBase),
                allowSceneObjects = false,
                value = _asset
            };
            _assetField.style.width = 260f;
            _assetField.RegisterValueChangedCallback(evt => SetAsset(evt.newValue as SceneFlowAssetBase));
            toolbar.Add(_assetField);

            toolbar.Add(new ToolbarButton(AddNodeAtViewCenter) { text = "ノードを追加" });
            toolbar.Add(new ToolbarButton(AddSimpleNodeAtViewCenter) { text = "シンプルノードを追加" });
            toolbar.Add(new ToolbarButton(ReloadFromAsset) { text = "再読み込み" });
            rootVisualElement.Add(toolbar);

            _graphView = new SceneFlowGraphView();
            _graphView.GraphChanged += RefreshMessages;
            rootVisualElement.Add(_graphView);

            _messageArea = new ScrollView();
            _messageArea.style.maxHeight = 140f;
            _messageArea.style.flexShrink = 0f;
            rootVisualElement.Add(_messageArea);

            ReloadFromAsset();
        }

        private void SetAsset(SceneFlowAssetBase asset)
        {
            _asset = asset;

            if (_assetField != null && _assetField.value != asset)
            {
                _assetField.SetValueWithoutNotify(asset);
            }

            ReloadFromAsset();
        }

        /// <summary>
        /// アセットの現在の内容を読み直す。CreateGUIより先に呼ばれることがあるので、
        /// UIができていない間は何もしない(CreateGUIの最後に改めて呼ばれる)。
        /// </summary>
        private void ReloadFromAsset()
        {
            if (_graphView == null) return;

            _serializer = _asset != null ? new SceneFlowGraphSerializer(_asset) : null;
            _graphView.Load(_serializer != null && _serializer.IsValid ? _serializer : null);

            RefreshMessages();
        }

        private void AddNodeAtViewCenter()
        {
            if (_graphView == null) return;

            _graphView.AddNodeAt(_graphView.GetViewCenter());
        }

        private void AddSimpleNodeAtViewCenter()
        {
            if (_graphView == null) return;

            _graphView.AddSimpleNodeAt(_graphView.GetViewCenter());
        }

        /// <summary>
        /// Undo/Redoではシリアライズデータが直接巻き戻るため、グラフを組み直して追従させる。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (_graphView == null || _asset == null) return;

            _graphView.Rebuild();
        }

        /// <summary>
        /// Buildが例外を投げる前に気づけるよう、検証結果をウィンドウ下部に出す。
        /// </summary>
        private void RefreshMessages()
        {
            if (_messageArea == null) return;

            _messageArea.Clear();

            if (_asset == null)
            {
                _messageArea.Add(new HelpBox("編集するSceneFlowアセットを選択してください。", HelpBoxMessageType.Info));
                return;
            }

            if (_serializer == null || !_serializer.IsValid)
            {
                _messageArea.Add(new HelpBox(
                    "このアセットからノード配列(_nodes)を読み取れませんでした。SceneFlowAsset<T>を継承しているか確認してください。",
                    HelpBoxMessageType.Error));
                return;
            }

            _serializer.Refresh();
            var messages = _serializer.Validate();

            if (messages.Count == 0)
            {
                _messageArea.Add(new HelpBox("遷移図に問題は見つかりませんでした。", HelpBoxMessageType.Info));
                return;
            }

            foreach (var message in messages)
            {
                _messageArea.Add(new HelpBox(message, HelpBoxMessageType.Warning));
            }
        }
    }
}
