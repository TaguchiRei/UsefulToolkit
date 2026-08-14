using System;
using System.Collections.Generic;
using UnityEngine;

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
    /// enumが出てくるのはこのアセットの中だけで、Buildが返す実行時表現から先はシーン名の文字列で扱う。
    /// </summary>
    public abstract class SceneFlowAsset<T> : SceneFlowAssetBase where T : Enum
    {
        [SerializeField] private SceneNodeData<T>[] _nodes;

        /// <summary>
        /// インスペクタで組んだ内容を実行時表現へ変換する。Initialization層で一度だけ呼ぶ想定。
        /// </summary>
        /// <exception cref="InvalidOperationException">NodeIdが重複しているときに出力</exception>
        public override SceneFlow Build()
        {
            var nodeDataList = _nodes ?? Array.Empty<SceneNodeData<T>>();
            var nodes = new List<SceneNode>(nodeDataList.Length);

            foreach (var nodeData in nodeDataList)
            {
                var groupDataList = nodeData.Groups ?? Array.Empty<SceneGroupData<T>>();
                var groups = new List<SceneGroup>(groupDataList.Length);

                foreach (var groupData in groupDataList)
                {
                    groups.Add(new SceneGroup(BuildSceneNames(groupData), groupData.ForceReload));
                }

                nodes.Add(new SceneNode(
                    nodeData.NodeId,
                    nodeData.DisplayName,
                    groups,
                    nodeData.NextNodeIds ?? Array.Empty<int>()));
            }

            try
            {
                return new SceneFlow(nodes);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"[{name}] のシーン遷移図が不正です。{exception.Message}", exception);
            }
        }

        /// <summary>
        /// Lighting/Content/Logic + Additional をシーン名へ変換して連結する。
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

            Add(groupData.Lighting);
            Add(groupData.Content);
            Add(groupData.Logic);

            foreach (var scene in groupData.Additional ?? Array.Empty<T>())
            {
                Add(scene);
            }

            return names.ToArray();
        }
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
        public T Lighting;
        public T Content;
        public T Logic;

        [Tooltip("上記3つに加えて読み込むシーン。不要なら空でよい")]
        public T[] Additional;

        [Tooltip("遷移元と共通のシーンも読み直すかどうか。" +
                 "オフだと共通シーンはUnloadもLoadもされず、状態がそのまま引き継がれる")]
        public bool ForceReload;
    }
}
