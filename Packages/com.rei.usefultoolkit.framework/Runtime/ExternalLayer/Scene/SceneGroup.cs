using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsefulToolkit.External.Scene
{
    /// <summary>
    /// 一まとまりでロード/アンロードするシーンの組。
    /// 内部で持つのは「先頭要素をアクティブシーンとして扱うか」のフラグ1つと、
    /// シーンID(ビルドインデックス)の配列1つだけ。
    ///
    /// <see cref="HasMainScene"/>がtrueなら配列の先頭がアクティブシーン、残りが追加シーン。
    /// falseなら全てが追加シーンで、ロードしてもアクティブシーンは変わらない。
    /// 「このグループを上書きロードするか追加ロードするか」はグループ自身は持たず、
    /// 呼び出し側(<c>SceneLoadService</c>)が決める。
    /// </summary>
    [Serializable]
    public sealed class SceneGroup : IEquatable<SceneGroup>
    {
        /// <summary> 配列の先頭要素をアクティブシーンとして扱うか </summary>
        [field: SerializeField]
        public bool HasMainScene { get; private set; }

        [SerializeField] private int[] _sceneIds;

        /// <summary>
        /// グループに属する全シーンID。<see cref="HasMainScene"/>がtrueなら先頭がアクティブシーン。
        /// 読み取り専用として扱うこと。
        /// </summary>
        public IReadOnlyList<int> SceneIds => _sceneIds;

        /// <summary> グループに含まれるシーン数 </summary>
        public int Count => _sceneIds.Length;

        /// <param name="hasMainScene">先頭要素をアクティブシーンとして扱うか</param>
        /// <param name="sceneIds">グループに属するシーンID。hasMainSceneがtrueなら先頭がアクティブシーン</param>
        /// <exception cref="ArgumentNullException">sceneIdsがnullのときに出力</exception>
        public SceneGroup(bool hasMainScene, int[] sceneIds)
        {
            if (sceneIds == null)
            {
                throw new ArgumentNullException(nameof(sceneIds));
            }

            HasMainScene = hasMainScene;
            _sceneIds = (int[])sceneIds.Clone();
        }

        /// <summary>
        /// アクティブシーンにするシーンIDを取得する。
        /// <see cref="HasMainScene"/>がfalse、または配列が空のときはfalseを返す。
        /// </summary>
        /// <param name="mainSceneId">アクティブシーンにするシーンID</param>
        /// <returns>アクティブシーンにするシーンが決まっているか</returns>
        public bool TryGetMainSceneId(out int mainSceneId)
        {
            if (HasMainScene && _sceneIds.Length > 0)
            {
                mainSceneId = _sceneIds[0];
                return true;
            }

            mainSceneId = -1;
            return false;
        }

        /// <summary>
        /// アクティブシーンと共にロードする追加シーンID。
        /// <see cref="HasMainScene"/>がtrueなら先頭を除いた範囲、falseなら全体。追加確保はしない。
        /// </summary>
        public ArraySegment<int> AdditiveSceneIds =>
            HasMainScene && _sceneIds.Length > 0
                ? new ArraySegment<int>(_sceneIds, 1, _sceneIds.Length - 1)
                : new ArraySegment<int>(_sceneIds);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(HasMainScene);

            foreach (var sceneId in _sceneIds)
            {
                hash.Add(sceneId);
            }

            return hash.ToHashCode();
        }

        public bool Equals(SceneGroup other)
        {
            if (ReferenceEquals(other, null))
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (HasMainScene != other.HasMainScene || _sceneIds.Length != other._sceneIds.Length)
                return false;

            for (int i = 0; i < _sceneIds.Length; i++)
            {
                if (_sceneIds[i] != other._sceneIds[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SceneGroup);
        }

        /// <summary>
        /// EnumからSceneGroupを構築する。
        /// </summary>
        /// <param name="hasMainScene">先頭要素をアクティブシーンとして扱うか</param>
        /// <param name="mainSceneEnum">アクティブシーンにするシーン。hasMainSceneがfalseなら無視される</param>
        /// <param name="additiveSceneEnums">共にロードするシーン</param>
        /// <typeparam name="T">ビルドシーンを表すEnum</typeparam>
        public static SceneGroup Create<T>(bool hasMainScene, T mainSceneEnum, T[] additiveSceneEnums)
            where T : Enum
        {
            additiveSceneEnums ??= Array.Empty<T>();

            var offset = hasMainScene ? 1 : 0;
            var sceneIds = new int[additiveSceneEnums.Length + offset];

            if (hasMainScene)
            {
                sceneIds[0] = Convert.ToInt32(mainSceneEnum);
            }

            for (int i = 0; i < additiveSceneEnums.Length; i++)
            {
                sceneIds[i + offset] = Convert.ToInt32(additiveSceneEnums[i]);
            }

            return new SceneGroup(hasMainScene, sceneIds);
        }
    }
}
