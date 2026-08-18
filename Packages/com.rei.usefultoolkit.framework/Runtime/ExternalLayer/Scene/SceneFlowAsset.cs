using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace UsefulToolkit.Framework.External
{
    /// <summary>
    /// シーン遷移図をインスペクタで組むためのScriptableObject。
    /// ジェネリックなScriptableObjectはそのままではアセット化できないため、
    /// 利用側で1行だけ非ジェネリックな派生を書くこと。
    /// <code>
    /// [CreateAssetMenu(menuName = "MyGame/SceneFlow")]
    /// public sealed class GameSceneFlow : SceneFlowAsset&lt;BuildScenes&gt; { }
    /// </code>
    /// </summary>
    public abstract class SceneFlowAsset<T> : SceneFlowAssetBase where T : Enum
    {
        [SerializeField] private SceneBootNodeData<T> _bootNode = new();

        [SerializeField] private SceneNodeData<T>[] _nodes;

        [SerializeField] private SceneSimpleNodeData<T>[] _simpleNodes;

        /// <summary>
        /// インスペクタで組んだ内容を実行時表現へ変換する。Initialization層で一度だけ呼ぶ想定。
        /// </summary>
        /// <exception cref="InvalidOperationException">NodeIdが重複している、または起動ノードが不正なときに出力</exception>
        public override SceneFlow Build()
        {
            var nodes = new List<SceneNode>();

            foreach (var nodeData in _nodes ?? Array.Empty<SceneNodeData<T>>())
            {
                var groupDataList = nodeData.Groups ?? Array.Empty<SceneGroupData<T>>();
                var groups = new List<SceneGroup>(groupDataList.Length);

                foreach (var groupData in groupDataList)
                {
                    groups.Add(new SceneGroup(
                        BuildSceneNames(groupData), groupData.Main.ToString(), groupData.ForceReload));
                }

                nodes.Add(new SceneNode(
                    nodeData.NodeId,
                    nodeData.DisplayName,
                    groups,
                    nodeData.NextNodeIds ?? Array.Empty<int>()));
            }

            // シンプルノードもBuild後は通常ノードと同じSceneNodeになるため、1つのリストへ合流させる
            foreach (var nodeData in _simpleNodes ?? Array.Empty<SceneSimpleNodeData<T>>())
            {
                var groupDataList = nodeData.Groups ?? Array.Empty<SceneSimpleGroupData<T>>();
                var groups = new List<SceneGroup>(groupDataList.Length);

                foreach (var groupData in groupDataList)
                {
                    groups.Add(new SceneGroup(
                        BuildSimpleSceneNames(groupData), groupData.Main.ToString(), groupData.ForceReload));
                }

                nodes.Add(new SceneNode(
                    nodeData.NodeId,
                    nodeData.DisplayName,
                    groups,
                    nodeData.NextNodeIds ?? Array.Empty<int>()));
            }

            var bootNode = _bootNode ?? new SceneBootNodeData<T>();

            try
            {
                return new SceneFlow(
                    nodes,
                    ToSceneNames(bootNode.PersistentScenes),
                    bootNode.EntryNodeId,
                    bootNode.EntryGroupIndex);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"[{name}] のシーン遷移図が不正です。{exception.Message}", exception);
            }
        }

        /// <summary>
        /// Main/Content/Logic + Additional をシーン名へ変換して連結する。
        /// 同じシーンを2回読み込もうとしないよう、重複は取り除く。
        /// </summary>
        private static string[] BuildSceneNames(SceneGroupData<T> groupData)
        {
            var names = new List<string>(3 + (groupData.Additional?.Length ?? 0));

            void Add(T scene)
            {
                var sceneName = scene.ToString();
                if (!names.Contains(sceneName)) names.Add(sceneName);
            }

            Add(groupData.Main);
            Add(groupData.Content);
            Add(groupData.Logic);

            foreach (var scene in groupData.Additional ?? Array.Empty<T>())
            {
                Add(scene);
            }

            return names.ToArray();
        }

        /// <summary>
        /// Main + Additional をシーン名へ変換して連結する。同じシーンを2回読み込もうとしないよう、
        /// 重複は取り除く。
        /// </summary>
        private static string[] BuildSimpleSceneNames(SceneSimpleGroupData<T> groupData)
        {
            var names = new List<string>(1 + (groupData.Additional?.Length ?? 0));

            void Add(T scene)
            {
                var sceneName = scene.ToString();
                if (!names.Contains(sceneName)) names.Add(sceneName);
            }

            Add(groupData.Main);

            foreach (var scene in groupData.Additional ?? Array.Empty<T>())
            {
                Add(scene);
            }

            return names.ToArray();
        }

        /// <summary> enumの並びを保ったままシーン名へ変換する。重複は取り除く </summary>
        private static string[] ToSceneNames(T[] scenes)
        {
            var sceneArray = scenes ?? Array.Empty<T>();
            var names = new List<string>(sceneArray.Length);

            foreach (var scene in sceneArray)
            {
                var sceneName = scene.ToString();
                if (!names.Contains(sceneName)) names.Add(sceneName);
            }

            return names.ToArray();
        }
    }

    /// <summary>
    /// 遷移図の起点になるBootノードのデータ。ノードエディタ上では削除できない1つだけのノードとして出る。
    /// ここに書いたシーンは起動時に一度だけ読み込まれ、その後はUnloadも読み直しもされない
    /// (ForceReloadの対象にもならない)。
    /// </summary>
    [Serializable]
    public sealed class SceneBootNodeData<T> where T : Enum
    {
        [Tooltip("ゲーム中ずっと読み込まれ続けるシーン。この順番で読み込まれる")]
        public T[] PersistentScenes;

        [Tooltip("起動時に最初に遷移するノードのID。ノードエディタ上でBootノードから線を引くと設定される")]
        public int EntryNodeId = SceneFlow.NoEntryNodeId;

        [Tooltip("起動時に読み込むシーングループのインデックス")]
        public int EntryGroupIndex;

#if UNITY_EDITOR
        /// <summary>
        /// ノードエディタ上での配置座標。編集のためだけの情報なのでビルドには持ち込まない。
        /// </summary>
        [HideInInspector] public Vector2 EditorPosition;
#endif
    }

    /// <summary> SceneFlowAssetのインスペクタ表示用データ。実行時にはSceneNodeへ変換される </summary>
    [Serializable]
    public sealed class SceneNodeData<T> where T : Enum
    {
        [Tooltip("SceneFlow内で一意なID")]
        public int NodeId;

        [Tooltip("ノードエディタやログでの表示名。実行時の挙動には影響しない")]
        public string DisplayName;

        [Tooltip("このノードで選べるシーンの組み合わせ")]
        public SceneGroupData<T>[] Groups;

        [Tooltip("このノードから遷移できるノードのID")]
        public int[] NextNodeIds;

#if UNITY_EDITOR
        /// <summary>
        /// ノードエディタ上での配置座標。編集のためだけの情報なのでビルドには持ち込まない。
        /// Buildはこのフィールドを参照しないため、実行時表現には影響しない。
        /// </summary>
        [HideInInspector] public Vector2 EditorPosition;
#endif
    }

    /// <summary> SceneFlowAssetのインスペクタ表示用データ。実行時にはSceneGroupへ変換される </summary>
    [Serializable]
    public sealed class SceneGroupData<T> where T : Enum
    {
        [Tooltip("通常の方式(Additional以外)で切り替えるメインのシーン。" +
                 "アクティブシーンとして扱われ、ライティング設定などはこのシーンのものが適用される")]
        [FormerlySerializedAs("Lighting")]
        public T Main;

        public T Content;
        public T Logic;

        [Tooltip("上記3つに加えて読み込むシーン。不要なら空でよい")]
        public T[] Additional;

        [Tooltip("遷移元と共通のシーンも読み直すかどうか。" +
                 "オフだと共通シーンはUnloadもLoadもされず、状態がそのまま引き継がれる")]
        public bool ForceReload;
    }

    /// <summary>
    /// SceneFlowAssetのインスペクタ表示用データ。実行時にはSceneNodeへ変換される。
    /// SceneNodeDataと違い、シーンをMain/Content/Logicの3枠に分けず、メインのシーン1つと
    /// 追加シーンの配列だけで組める——一枚絵のタイトル画面のように、役割分けする意味がない
    /// シーン構成のためのノード種別。
    /// </summary>
    [Serializable]
    public sealed class SceneSimpleNodeData<T> where T : Enum
    {
        [Tooltip("SceneFlow内で一意なID")]
        public int NodeId;

        [Tooltip("ノードエディタやログでの表示名。実行時の挙動には影響しない")]
        public string DisplayName;

        [Tooltip("このノードで選べるシーンの組み合わせ")]
        public SceneSimpleGroupData<T>[] Groups;

        [Tooltip("このノードから遷移できるノードのID")]
        public int[] NextNodeIds;

#if UNITY_EDITOR
        /// <summary>
        /// ノードエディタ上での配置座標。編集のためだけの情報なのでビルドには持ち込まない。
        /// Buildはこのフィールドを参照しないため、実行時表現には影響しない。
        /// </summary>
        [HideInInspector] public Vector2 EditorPosition;
#endif
    }

    /// <summary>
    /// SceneSimpleNodeDataのインスペクタ表示用データ。実行時にはSceneGroupへ変換される。
    /// SceneGroupDataからContent/Logicの2枠を取り除いたもの。
    /// </summary>
    [Serializable]
    public sealed class SceneSimpleGroupData<T> where T : Enum
    {
        [Tooltip("通常の方式で切り替えるメインのシーン。" +
                 "アクティブシーンとして扱われ、ライティング設定などはこのシーンのものが適用される")]
        public T Main;

        [Tooltip("メインシーンに加えて読み込むシーン。不要なら空でよい")]
        public T[] Additional;

        [Tooltip("遷移元と共通のシーンも読み直すかどうか。" +
                 "オフだと共通シーンはUnloadもLoadもされず、状態がそのまま引き継がれる")]
        public bool ForceReload;
    }
}
