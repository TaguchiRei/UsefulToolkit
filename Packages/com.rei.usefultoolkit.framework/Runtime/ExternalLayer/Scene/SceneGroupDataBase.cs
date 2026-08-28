using System;
using UnityEngine;

namespace UsefulToolkit.External.Scene
{
    /// <summary>
    /// 一まとまりでロードするシーンの組を、Enumで編集できる形で保持するアセットの基底。
    /// </summary>
    /// <typeparam name="TSceneEnum">ビルドシーンを表すEnum</typeparam>
    public abstract class SceneGroupDataBase<TSceneEnum> : ScriptableObject, ISerializationCallbackReceiver
        where TSceneEnum : Enum
    {
#if UNITY_EDITOR
        public TSceneEnum ActiveScene;
        public TSceneEnum[] AdditionalScenes;

        /// <summary>
        /// このグループをロードする際、グループへ含まれないロード済みシーンをアンロードするかどうか。
        /// </summary>
        public bool OverwriteLoadedScenes;
#endif
        //実際に運用されるデータ
        public SceneGroup GroupData => _groupData;
        [SerializeField] private SceneGroup _groupData;

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            _groupData = SceneGroup.Create(ActiveScene, AdditionalScenes ?? Array.Empty<TSceneEnum>(),
                OverwriteLoadedScenes);
#endif
        }

        public void OnAfterDeserialize()
        {
        }
    }
}
