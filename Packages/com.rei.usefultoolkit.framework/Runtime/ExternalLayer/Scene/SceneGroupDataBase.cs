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

#if UNITY_EDITOR
        /// <summary>
        /// メインシーンのEnum値に対応するシーン名。
        /// ビルドインデックスが変わっても、この名前を手掛かりに参照を貼り直すために保持する。
        /// </summary>
        [SerializeField] protected string _mainSceneName;

        /// <summary> 追加シーンのEnum値に対応するシーン名。並びは追加シーン配列と対応する。 </summary>
        [SerializeField] protected string[] _additionalSceneNames;
#endif

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

        /// <summary>
        /// 保持しているシーン名を手掛かりに、Enumフィールドを現在のビルドシーンへ貼り直す。
        /// 名前で解決できずインデックスで解決した場合、その名前は書き換えずに残す。
        /// </summary>
        /// <returns>貼り直しの結果</returns>
        public abstract SceneGroupRebindResult RebindSceneReferences();

        /// <summary>
        /// 現在のビルドシーンに対して、保持しているシーン参照が解決できるかを調べる。資産は書き換えない。
        /// </summary>
        /// <returns>調査結果</returns>
        public abstract SceneGroupRebindResult InspectSceneReferences();
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

        public override SceneGroupRebindResult RebindSceneReferences()
        {
            return Classify(applyChanges: true);
        }

        public override SceneGroupRebindResult InspectSceneReferences()
        {
            return Classify(applyChanges: false);
        }

        /// <summary>
        /// メインシーンと追加シーンの各Enum値を、保持しているシーン名を手掛かりに分類する。
        /// applyChangesがtrueなら、解決できたものはEnumフィールドを書き換える。
        /// </summary>
        /// <param name="applyChanges">解決結果をEnumフィールドへ反映するか</param>
        private SceneGroupRebindResult Classify(bool applyChanges)
        {
            var memberNames = Enum.GetNames(typeof(TSceneEnum));

            bool hasRemovedScene = false;
            bool hasIndexResolvedScene = false;
            bool changed = false;

            if (applyChanges)
            {
                EnsureNameArrayLength();
            }

            if (HasMainScene)
            {
                var entry = ClassifyEntry(_mainScene, _mainSceneName, memberNames,
                    applyChanges, out var newValue, out var newName);

                if (applyChanges && entry.Changed)
                {
                    _mainScene = newValue;
                    if (newName != null)
                    {
                        _mainSceneName = newName;
                    }

                    changed = true;
                }

                hasRemovedScene |= entry.Removed;
                hasIndexResolvedScene |= entry.IndexResolved;
            }

            if (_additionalScenes != null)
            {
                for (int i = 0; i < _additionalScenes.Length; i++)
                {
                    string storedName = _additionalSceneNames != null && i < _additionalSceneNames.Length
                        ? _additionalSceneNames[i]
                        : null;

                    var entry = ClassifyEntry(_additionalScenes[i], storedName, memberNames,
                        applyChanges, out var newValue, out var newName);

                    if (applyChanges && entry.Changed)
                    {
                        _additionalScenes[i] = newValue;
                        if (newName != null)
                        {
                            _additionalSceneNames[i] = newName;
                        }

                        changed = true;
                    }

                    hasRemovedScene |= entry.Removed;
                    hasIndexResolvedScene |= entry.IndexResolved;
                }
            }

            return new SceneGroupRebindResult(hasRemovedScene, hasIndexResolvedScene, changed);
        }

        /// <summary>
        /// 1つのEnum値を、対応するシーン名を手掛かりに分類する。
        /// 1. シーン名が現在のメンバーにあれば名前で解決する。
        /// 2. 無ければ、現在の値が有効なEnum値ならインデックスで解決する。
        ///    シーン名が空(移行前の資産)なら名前を補完し、そうでなければ名前を残したまま警告対象にする。
        /// 3. どちらでも解決できなければ解決不能として警告対象にする。
        /// </summary>
        /// <param name="current">現在のEnum値</param>
        /// <param name="storedName">対応するシーン名。nullや空もあり得る</param>
        /// <param name="memberNames">現在のEnumのメンバー名一覧</param>
        /// <param name="applyChanges">解決結果を返すか(falseなら分類のみ)</param>
        /// <param name="newValue">applyChangesかつChangedのとき、書き込むEnum値</param>
        /// <param name="newName">nullでなければ、書き込むシーン名</param>
        private static EntryResult ClassifyEntry(TSceneEnum current, string storedName, string[] memberNames,
            bool applyChanges, out TSceneEnum newValue, out string newName)
        {
            newValue = current;
            newName = null;

            if (!string.IsNullOrEmpty(storedName) && Array.IndexOf(memberNames, storedName) >= 0)
            {
                var resolved = (TSceneEnum)Enum.Parse(typeof(TSceneEnum), storedName);
                bool differs = Convert.ToInt32(resolved) != Convert.ToInt32(current);
                if (applyChanges && differs)
                {
                    newValue = resolved;
                    return new EntryResult(removed: false, indexResolved: false, changed: true);
                }

                return new EntryResult(removed: false, indexResolved: false, changed: false);
            }

            int currentInt = Convert.ToInt32(current);
            if (Enum.IsDefined(typeof(TSceneEnum), currentInt))
            {
                bool nameWasProvided = !string.IsNullOrEmpty(storedName);
                if (!nameWasProvided)
                {
                    if (applyChanges)
                    {
                        newName = Enum.GetName(typeof(TSceneEnum), current);
                        return new EntryResult(removed: false, indexResolved: false, changed: true);
                    }

                    return new EntryResult(removed: false, indexResolved: false, changed: false);
                }

                return new EntryResult(removed: false, indexResolved: true, changed: false);
            }

            return new EntryResult(removed: true, indexResolved: false, changed: false);
        }

        /// <summary> 追加シーン名配列の長さを、追加シーン配列の長さへ合わせる。 </summary>
        private void EnsureNameArrayLength()
        {
            int length = _additionalScenes?.Length ?? 0;
            if (_additionalSceneNames == null || _additionalSceneNames.Length != length)
            {
                Array.Resize(ref _additionalSceneNames, length);
            }
        }

        private readonly struct EntryResult
        {
            public readonly bool Removed;
            public readonly bool IndexResolved;
            public readonly bool Changed;

            public EntryResult(bool removed, bool indexResolved, bool changed)
            {
                Removed = removed;
                IndexResolved = indexResolved;
                Changed = changed;
            }
        }
#endif
    }
}
