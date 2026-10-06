namespace UsefulToolkit.MeshCut
{
    /// <summary> CuttableObject.AdoptCutShape で、形を移した先の当たり判定をどう作るか </summary>
    public enum AdoptColliderMode
    {
        /// <summary>
        /// 破片の球コライダーを写し、元から持っていたコライダーを無効にする。
        /// 球はメッシュの頂点から求めるため、頂点の少ない長い形では球の並びに隙間ができることがある
        /// </summary>
        Spheres,

        /// <summary>
        /// 元から持っていたコライダーを、移したメッシュの bounds を覆う大きさに合わせて初期化の時点の有効・無効のまま使い、球コライダーは無効にする。
        /// メッシュと交わる平面は必ずコライダーとも交わる。合わせられるコライダーが無いときは Spheres と同じにする
        /// </summary>
        FitOwnColliders
    }
}
