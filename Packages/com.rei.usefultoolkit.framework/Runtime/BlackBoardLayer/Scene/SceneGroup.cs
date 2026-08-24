using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard
{
    [Serializable]
    public sealed class SceneGroup : IEquatable<SceneGroup>
    {
        public string MainSceneName { get; }
        public IReadOnlyList<string> SubSceneNames => _subSceneNames;

        private readonly string[] _subSceneNames;

        public SceneGroup(string mainSceneName, string[] subSceneNames)
        {
            MainSceneName = mainSceneName;
            _subSceneNames = (string[])subSceneNames.Clone();
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(MainSceneName);

            foreach (var scene in _subSceneNames)
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

            if (MainSceneName != other.MainSceneName ||
                _subSceneNames.Length != other._subSceneNames.Length)
                return false;

            for (int i = 0; i < _subSceneNames.Length; i++)
            {
                if (_subSceneNames[i] != other._subSceneNames[i])
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
        /// <param name="mainSceneName"></param>
        /// <param name="subSceneNames"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static SceneGroup Create<T>(T mainSceneName, T[] subSceneNames) where T : Enum
        {
            var subSceneNamesString = new string[subSceneNames.Length];

            for (int i = 0; i < subSceneNames.Length; i++)
            {
                subSceneNamesString[i] = subSceneNames[i].ToString();
            }

            return new SceneGroup(mainSceneName.ToString(), subSceneNamesString);
        }
    }
}