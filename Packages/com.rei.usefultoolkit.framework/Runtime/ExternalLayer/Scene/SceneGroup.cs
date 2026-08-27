using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.Scene
{
    [Serializable]
    public sealed class SceneGroup : IEquatable<SceneGroup>
    {
        public int MainSceneId { get; }
        public IReadOnlyList<int> SubSceneIds => _subSceneIds;

        private readonly int[] _subSceneIds;

        public SceneGroup(int mainSceneID, int[] subSceneIds)
        {
            MainSceneId = mainSceneID;
            _subSceneIds = (int[])subSceneIds.Clone();
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(MainSceneId);

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
        /// <param name="mainSceneEnum"></param>
        /// <param name="subSceneEnums"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static SceneGroup Create<T>(T mainSceneEnum, T[] subSceneEnums) where T : Enum
        {
            var subSceneNamesInt = new int[subSceneEnums.Length];

            for (int i = 0; i < subSceneEnums.Length; i++)
            {
                subSceneNamesInt[i] = Convert.ToInt32(subSceneEnums[i]);
            }

            return new SceneGroup(Convert.ToInt32(mainSceneEnum), subSceneNamesInt);
        }
    }
}