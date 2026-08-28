using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsefulToolkit.External.Scene
{
    /// <summary>
    /// 一まとまりでロードするシーンの組。
    /// アクティブシーンにするシーンIDと、共にロードするシーンIDを保持する。
    /// </summary>
    [Serializable]
    public sealed class SceneGroup : IEquatable<SceneGroup>
    {
        /// <summary> アクティブシーンにするシーンID </summary>
        [field: SerializeField]
        public int MainSceneId { get; private set; }

        /// <summary> アクティブシーンと共にロードするシーンID </summary>
        public IReadOnlyList<int> SubSceneIds => _subSceneIds;

        /// <summary>
        /// ロード時に、このグループへ含まれないロード済みシーンをアンロードするかどうか。
        /// falseの場合、既にロードされているシーンはそのまま残り、このグループが追加でロードされる。
        /// </summary>
        [field: SerializeField]
        public bool OverwriteLoadedScenes { get; private set; }

        [SerializeField] private int[] _subSceneIds;

        public SceneGroup(int mainSceneId, int[] subSceneIds, bool overwriteLoadedScenes)
        {
            MainSceneId = mainSceneId;
            OverwriteLoadedScenes = overwriteLoadedScenes;
            _subSceneIds = (int[])subSceneIds.Clone();
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(MainSceneId);
            hash.Add(OverwriteLoadedScenes);

            foreach (var scene in _subSceneIds)
            {
                hash.Add(scene);
            }

            return hash.ToHashCode();
        }

        public bool Equals(SceneGroup other)
        {
            if (ReferenceEquals(other, null))
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (MainSceneId != other.MainSceneId ||
                OverwriteLoadedScenes != other.OverwriteLoadedScenes ||
                _subSceneIds.Length != other._subSceneIds.Length)
                return false;

            for (int i = 0; i < _subSceneIds.Length; i++)
            {
                if (_subSceneIds[i] != other._subSceneIds[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SceneGroup);
        }

        /// <summary>
        /// EnumからSceneGroupを構築する
        /// </summary>
        /// <param name="mainSceneEnum">アクティブシーンにするシーン</param>
        /// <param name="subSceneEnums">共にロードするシーン</param>
        /// <param name="overwriteLoadedScenes">このグループへ含まれないロード済みシーンをアンロードするか</param>
        /// <typeparam name="T">ビルドシーンを表すEnum</typeparam>
        public static SceneGroup Create<T>(T mainSceneEnum, T[] subSceneEnums, bool overwriteLoadedScenes)
            where T : Enum
        {
            var subSceneNamesInt = new int[subSceneEnums.Length];

            for (int i = 0; i < subSceneEnums.Length; i++)
            {
                subSceneNamesInt[i] = Convert.ToInt32(subSceneEnums[i]);
            }

            return new SceneGroup(Convert.ToInt32(mainSceneEnum), subSceneNamesInt, overwriteLoadedScenes);
        }
    }
}