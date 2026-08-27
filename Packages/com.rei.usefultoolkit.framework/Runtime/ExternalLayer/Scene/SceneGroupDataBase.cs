using System;
using UnityEngine;
using UsefulToolkit.BlackBoard.Scene;

namespace UsefulToolkit.External
{
    [CreateAssetMenu(fileName = "SceneGroupData", menuName = "Scriptable Objects/UsefulToolkit/SceneGroupData")]
    public abstract class SceneGroupDataBase<TSceneEnum> : ScriptableObject, ISerializationCallbackReceiver
        where TSceneEnum : Enum
    {
#if UNITY_EDITOR
        public TSceneEnum ActiveScene;
        public TSceneEnum[] AdditionalScenes;
#endif
        //実際に運用されるデータ
        public SceneGroup GroupData => _groupData;
        [SerializeField] private SceneGroup _groupData;

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            _groupData = SceneGroup.Create(ActiveScene, AdditionalScenes);
#endif
        }

        public void OnAfterDeserialize()
        {
        }
    }
}