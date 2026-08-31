using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.External.Scene;

namespace UsefulToolkit.Editor.SceneSupport
{
    /// <summary>
    /// SceneGroup資産のシーン参照を、ビルドシーンの並び替え・リネームに追従させる。
    /// ドメインリロードのたびに、保持しているシーン名を手掛かりにEnumフィールドを貼り直し(Rebind)、
    /// 解決できなかったものをコンソールへ警告する(Validate)。
    /// enum再生成はファイル書き出し後の再コンパイルとドメインリロードを伴うため、
    /// リロード後に走るこの処理が新しいenumに対して貼り直しを行う。
    /// </summary>
    [InitializeOnLoad]
    public static class SceneGroupReferenceMaintainer
    {
        internal const string RemovedSceneMessage =
            "ビルドから除かれたシーンを含むSceneGroupが定義されています";

        internal const string IndexResolvedMessage =
            "Indexでシーンを解決したため、間違ったシーンを含む可能性のあるSceneGroupが定義されています";

        static SceneGroupReferenceMaintainer()
        {
            EditorApplication.delayCall += RebindAndValidate;
        }

        /// <summary>
        /// 全SceneGroup資産を貼り直し、続けて検証する。
        /// </summary>
        [MenuItem("UsefulToolkit/Scene/Rebind SceneGroups", false, 21)]
        public static void RebindAndValidate()
        {
            Rebind();
            Validate();
        }

        /// <summary>
        /// 全SceneGroup資産を、保持しているシーン名を手掛かりに現在のビルドシーンへ貼り直す。
        /// 実際に変わった資産だけを保存する。
        /// </summary>
        private static void Rebind()
        {
            bool anyChanged = false;

            foreach (var asset in LoadAll())
            {
                if (asset.RebindSceneReferences().Changed)
                {
                    EditorUtility.SetDirty(asset);
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// 全SceneGroup資産のシーン参照を調べ、解決できなかったものをコンソールへ警告する。
        /// 警告の第2引数に資産を渡すため、クリックで該当資産へ移動できる。
        /// </summary>
        private static void Validate()
        {
            foreach (var asset in LoadAll())
            {
                var result = asset.InspectSceneReferences();

                if (result.HasRemovedScene)
                {
                    Debug.LogWarning($"{RemovedSceneMessage}\n対象: {AssetLabel(asset)}", asset);
                }

                if (result.HasIndexResolvedScene)
                {
                    Debug.LogWarning($"{IndexResolvedMessage}\n対象: {AssetLabel(asset)}", asset);
                }
            }
        }

        private static string AssetLabel(SceneGroupDataBase asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? asset.name : path;
        }

        private static IEnumerable<SceneGroupDataBase> LoadAll()
        {
            return AssetDatabase.FindAssets($"t:{nameof(SceneGroupDataBase)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(AssetDatabase.LoadAssetAtPath<SceneGroupDataBase>)
                .Where(asset => asset != null)
                .Distinct();
        }
    }
}
