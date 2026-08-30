using System;
using UnityEngine;

namespace UsefulToolkit.External.Scene
{
    /// <summary>
    /// シーングループデータアセットの非ジェネリック基底。
    /// エディタ拡張やInitialization層が、ビルドシーンEnumの型を知らずに
    /// 運用データ(<see cref="GroupData"/>)へ触れられるようにするための土台。
    /// 実際の編集用フィールドはジェネリックな<see cref="SceneGroupDataBase{TSceneEnum}"/>が持つ。
    /// </summary>
    public abstract class SceneGroupDataBase : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// オンのとき、先頭のシーンをアクティブシーンとしてロードする。
        /// オフのときは全て追加シーンで、ロードしてもアクティブシーンは変わらない。
        /// Inspectorの表示切り替えにも使う。
        /// </summary>
        [SerializeField] private bool _hasMainScene;

        // 実際に運用されるデータ
        public SceneGroup GroupData => _groupData;
        [SerializeField] private SceneGroup _groupData;

        /// <summary> 先頭のシーンをアクティブシーンとして扱うか </summary>
        protected bool HasMainScene => _hasMainScene;

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            _groupData = BuildGroupData(_hasMainScene);
#endif
        }

        public void OnAfterDeserialize()
        {
        }

#if UNITY_EDITOR
        /// <summary>
        /// エディタで編集中のEnumフィールドから運用データを組み立てる。
        /// </summary>
        /// <param name="hasMainScene">先頭シーンをアクティブシーンとして扱うか</param>
        protected abstract SceneGroup BuildGroupData(bool hasMainScene);
#endif
    }

    /// <summary>
    /// 一まとまりでロードするシーンの組を、Enumで編集できる形で保持するアセットの基底。
    /// </summary>
    /// <typeparam name="TSceneEnum">ビルドシーンを表すEnum</typeparam>
    public abstract class SceneGroupDataBase<TSceneEnum> : SceneGroupDataBase
        where TSceneEnum : Enum
    {
#if UNITY_EDITOR
        [SerializeField] private TSceneEnum _mainScene;
        [SerializeField] private TSceneEnum[] _additionalScenes;

        protected override SceneGroup BuildGroupData(bool hasMainScene)
        {
            return SceneGroup.Create(
                hasMainScene,
                _mainScene,
                _additionalScenes ?? Array.Empty<TSceneEnum>());
        }
#endif
    }
}
