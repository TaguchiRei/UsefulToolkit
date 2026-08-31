namespace UsefulToolkit.External.Scene
{
    /// <summary>
    /// <see cref="SceneGroupDataBase"/> のシーン参照を貼り直した／調べた結果。
    /// </summary>
    public readonly struct SceneGroupRebindResult
    {
        /// <summary> 名前でもインデックスでも解決できないシーン参照があるか。 </summary>
        public readonly bool HasRemovedScene;

        /// <summary> 名前で解決できず、インデックスで解決したシーン参照があるか。 </summary>
        public readonly bool HasIndexResolvedScene;

        /// <summary> 貼り直しによってEnumフィールドまたはシーン名が実際に変わったか。 </summary>
        public readonly bool Changed;

        public SceneGroupRebindResult(bool hasRemovedScene, bool hasIndexResolvedScene, bool changed)
        {
            HasRemovedScene = hasRemovedScene;
            HasIndexResolvedScene = hasIndexResolvedScene;
            Changed = changed;
        }

        /// <summary> 警告を要する状態か。 </summary>
        public bool HasWarning => HasRemovedScene || HasIndexResolvedScene;
    }
}
