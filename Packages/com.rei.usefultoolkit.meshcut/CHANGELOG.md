# Changelog

## [Unreleased]

### Changed

- 切断処理の全Jobを1本の依存チェーンでスケジュールし、完了をメインスレッドで1回だけ待つようにしました。
  従来は段階ごとに完了を待っていたため、計算量に関係なく結果が出るまで最低8フレームかかっていました。
  - 切断三角形数で決まるバッファは `NativeList` にし、新設の `CutFacePrefixSumJob` の中でサイズを決めます。
    `TriangleCutJob` は `IJobParallelForDefer` になりました。
  - 断面の辺を受け渡していた `NativeParallelMultiHashMap`(`CutEdges`)を廃止しました。
    切断三角形 i の辺は常に新規頂点 (2i, 2i+1) なので、`DistributeAndCapJob` が直接求めます。
  - サンプリング範囲の計算を `SampleRangeJob` に、メッシュへの書き込みを `FinalizeMeshJob`(フラグメント単位で並列)に移し、
    バックグラウンドスレッドとの切り替えをなくしました。
- フラグメントバッファの確保量を、最悪ケース(全三角形が切断される想定)から実数ベースに変えました。確保量はおよそ1/17〜1/38です。
  - `ClassifyWholeMeshJob` は数えるだけになり、表裏それぞれの頂点数・サブメッシュ別インデックス数・切断三角形数を出力します。
  - 新設の `FragmentLayoutJob` がその実数から容量と書き込み位置を決め、フラットなリスト(`NativeList`)を確保します。
  - 新設の `WriteWholeTrianglesJob` が、丸ごと入る三角形の書き込みと切断面リストの構築を1回の走査で行います。
    これに伴い `BuildCutFaceListJob` を廃止しました。
- 切断のたびに切断対象の頂点・三角形データを複製していた `CopyMeshDataJob` を廃止し、各Jobが `NativeMeshDataStore` を
  直接読むようにしました。頂点はオブジェクトごとの通し番号で扱い、ストア上の位置はオブジェクトごとのずれ
  (`ObjectStoreVertexOffset`)から求めます。複製用の配列(頂点あたり36バイト・三角形あたり16バイト)が不要になりました。
  - ストアを読む Job が走っている間にストアが変更されないよう、`MeshDataCache` に `AddStoreReader` /
    `CompleteStoreReaders` を追加し、ストアの変更・破棄の前に読み取り中の Job を完了させるようにしました。
- 切断で増える三角形(切断三角形を分割した側面の三角形と、断面のファン三角形)の頂点を重複除去するようにしました。
  破片の頂点数は 11〜42% 減ります(切断面付近の三角形の割合が大きい、ポリゴン数の少ないメッシュほど減ります)。
  - 側面の頂点は、元からある頂点なら通し番号、切断でできた頂点なら切断した元の辺(`NewVertexEdge`)をキーに表裏それぞれで共有します。
    座標でまとめないのは、UV の継ぎ目のように位置が同じでも属性の違う頂点を1つにしないためです。
  - 断面の頂点はループの各頂点と中心に1つずつ置いて共有します。側面とは法線が違うため共有しません。
- 生成したメッシュの適用(`Mesh.ApplyAndDisposeWritableMeshData`)で、Unity 側のインデックス検証を省くようにしました
  (`MeshUpdateFlags.DontValidateIndices`)。インデックスは各フラグメントの頂点数の範囲内でしか書かれないためです。
- 破片の球コライダーを求める k-means を、メインスレッドから Burst の `ColliderClusterJob`(破片単位で並列)へ移しました。
  `MultiCutBlade` は全破片ぶんをまとめて計算し、破片反映では結果をコライダーへ設定するだけになりました。
  `CuttableObject.SetupCollider(List<Vector3>)` は同じJobを破片1つぶん実行する形で残しています。
  初期中心の乱数は `Unity.Mathematics.Random` に変わったため、同じ入力でも以前とは異なる初期配置になります。

- 処理時間の計測を作り直しました。従来は各段階の間を Stopwatch で測っていたため、Job の完了を待つフレーム待ちまで
  処理時間に含まれていました。現在は段階ごとに「待ち / 実行 / 検知遅れ / フレーム」を分けて記録し、
  切断 1 回ぶんを 1 つの表として出力します。Job の実行時間はワーカー上で記録します。
- `MultiCutBlade` の計測結果に、プール生成待ち・破片取得・破片反映(うち `SetupCollider`)を含めるようにしました。
  破片反映をフレーム分割したときの個別ログは廃止し、表の付帯情報「破片反映のフレーム分割回数」にまとめました。

### Added

- `MultiCutBlade.ExecuteCut` が、切断した対象ごとの元の対象と表裏の破片の組(`MultiCutResult[]`)を返すようにしました。
  戻り値は `UniTask` から `UniTask<MultiCutResult[]>` に変わりましたが、`await ExecuteCut(...)` の呼び出しはそのまま動きます。
  すべての破片への反映が終わってから返し、何も切断しなかった場合は空の配列を返します。
  - インスペクタの右クリックメニュー「切断」は、切断結果の組を Console に出力するようになりました。
- `MultiCutResult`
- `MeshCutProfile` / `MeshCutStageRecord` / `MeshCutProfileInfo` / `MeshCutStageKind`
- `MultiMeshCut.LastProfile`、`MultiCutBlade.LastProfile` / `EnableProfileLog` / `CollectProfile`
- 計測結果の付帯情報に、閉じた断面ループ数・途切れた断面ループ区間数・断面が生成されなかった対象数を追加しました。
- `ColliderClusterJob` / `ColliderClusterSettings`、`CuttableObject.ColliderSettings` / `ApplyColliderSpheres` / `SetCutMesh`
- `MeshDataCache.AddStoreReader` / `CompleteStoreReaders`
- `MeshDataCache.Register` — 実行中に 1 つの `CuttableObject` を登録し、切断できる状態にします。
  非アクティブなものや `MeshDataCache` の子でないものも登録でき、同じ共有メッシュを何度登録してもストアへの追加は 1 回だけです。
  - `NativeMeshDataStore.Add` に断面サブメッシュの番号を渡す省略可能な引数を足しました。切断後のメッシュを登録したとき、次の切断で断面を増やさずそこへ追記させるためです。
- `CuttableObject.MaxCutCount` / `CutCount` — 部位の系統(そのオブジェクトと、そこから生まれた破片)ごとの切断回数の上限(Inspector の **Max Cut Count**、既定 0 = 上限なし)と、これまでに切られた回数。
  破片は切断元の上限と「切断元の回数 + 1」を引き継ぎ、回数が上限に達した破片は切れなくなります。上限は Can Multi Cut が有効なときだけ効きます。
  - `CuttableObject.InheritCutSettings(CuttableObject)` は、上限と回数も引き継ぐようになりました。
- `CuttableObject.AdoptCutShape` — 破片の切断後の形(メッシュの持ち主・マテリアル・球コライダー・切断設定と回数・切断可否)を自分に移し、元から持っていたコライダーを無効にします。
  移したあとの破片はプールへ返して使い回せます。
- `CuttableObject.RestoreInitialShape` — 初期化の時点のメッシュ・マテリアル・コライダー・切断設定に戻し、切断回数を 0 にします。
- 一度もアクティブになっていない(`Awake` が走っていない) `CuttableObject` も、上の操作で扱えるようにしました。

### Fixed

- (移植元から引き継いだ不具合) 拡大率が軸ごとに異なるオブジェクトを斜めに切ると、切断面の向きがずれていた問題。
  `BladeToLocalJob` が刃の法線をローカル空間へ移すときに拡大率で割っていました。法線は拡大率を掛けて変換し、正規化するようにしました。
  断面の頂点法線にもこの値が入るため、断面の法線も単位長になります。
- (移植元から引き継いだ不具合) 切断で生成したメッシュの `Mesh.bounds` が大きさ0のままだった問題。`Mesh.ApplyAndDisposeWritableMeshData` は
  サブメッシュの bounds しか反映しないため、切断元の位置が画面外に出ると破片が見えていても描画されずに消えていました。
  インデックスを持つサブメッシュの bounds を合わせて設定するようにしました。
  `Renderer.bounds` から求めている右クリックメニュー「切断」のログの side も、これで正しい向きになります。
- 何回でも切断できる破片を切り直したとき、プールが 1 回の切断の中で一周して、切断元の破片が同じ切断の破片として配られると、
  破片の位置・マテリアル・速度・切断設定が別の対象のものになったり、反映したばかりの破片が非アクティブにされたりしていた問題。
  `MultiCutBlade` は、破片を取り出す前に切断元の Transform・マテリアル・`CanMultiCut`・速度を読み取っておいて反映に使い、
  既に破片として反映した切断元は非アクティブにしないようにしました。
- 断面ループが閉じずにキャップが生成されなかったフラグメント(末尾のサブメッシュが0件)があると、
  `FinalizeMeshes` の `NativeArray.Copy` が範囲外例外を投げて切断全体が失敗していた問題。
  0件のコピーを行わないようにしました(頂点数0のフラグメントも同様)。
- 断面ループが途切れてキャップ(断面)が生成されないことがあった問題。
  同じ辺を共有する隣り合う三角形で補間の向きが逆になり、交点が最後の桁でずれて、
  ループ探索の量子化(0.1mm)の境目をまたいだときに別の点として扱われていたためです。
  `TriangleCutJob` で辺の端点を座標の辞書順に揃えてから補間し、同じ辺からは常に同じ交点が出るようにしました。
- 破片を使い回すたびに、前回の切断で生成したメッシュが破棄されずに残っていた問題(`Resources.UnloadUnusedAssets` を
  呼ぶまでネイティブメモリが増え続けていた)。破片が切断で生成されたメッシュの持ち主になり、
  差し替え時と破片の破棄時に `Destroy` するようにしました(`CuttableObject.SetCutMesh`)。
- 閉じた断面ループの最後の辺を探索済みにしていなかったため、同じループを逆向きに辿り直す無駄な探索が走っていた問題。

## [1.0.0] - UsefulToolkit への移植

`TaguchiRei/MeshCut` の `com.rei.usefulmeshcut` を UsefulToolkit のサブパッケージとして取り込んだものです。
切断アルゴリズムそのものには変更を加えていません。

### Changed

- パッケージ名を `com.rei.usefultoolkit.meshcut` に変更しました。
- 名前空間を `UsefulMeshCut` → `UsefulToolkit.MeshCut` に変更しました(Toolkit の規約に合わせ、Editor 側も同じ名前空間に置いています)。
- asmdef を Toolkit の命名規約に合わせ `UsefulToolkit.MeshCut.Runtime` / `UsefulToolkit.MeshCut.Editor` に変更しました。
- `RecycleBuffer<T>` と `IRecyclable` を `com.rei.usefultoolkit.framework` の `UsefulToolkit.Utility`
  (asmdef `UsefulToolkit.Utility`) へ移しました。MeshCut 固有の型ではなく汎用ユーティリティのためです。
  これに伴い Framework パッケージが依存に加わりました。
- メニューを `UsefulTools > UsefulMesh > MeshCut > Setup` から `UsefulToolkit > Mesh Cut > Setup` へ移動しました。
- セットアップウィンドウが生成するルートオブジェクト名を `UsefulMeshCut System` → `MeshCut System` に変更しました。
- ログの接頭辞を `[UsefulMeshCut]` → `[UsefulToolkit.MeshCut]` に変更しました。

## [1.0.0] - 移植元リリース

初回リリース。開発用プロジェクトの MeshCut Version4 を UPM パッケージとして切り出したものです。

### Added

- Burst コンパイル済み Job チェーンによる複数メッシュの一括切断 (`MultiMeshCut`)
- 刃コンポーネントと破片への反映処理 (`MultiCutBlade`)
- メッシュデータの Native キャッシュ (`MeshDataCache` / `NativeMeshDataStore`)
- 破片プール (`MeshCutObjectPool`)
- セットアップウィンドウ `UsefulToolkit > Mesh Cut > Setup`
  - シーンへの `MeshDataCache` / `FragmentPool` / `CutBlade` の生成
  - 選択オブジェクトの切断可能化と `MeshDataCache` 配下への移動
  - 配置状況と、キャッシュ配下にない `CuttableObject` の検出
- **1回だけ切断可能 / 何回でも切断可能** の切り替え (`CuttableObject.CanMultiCut`)
  - 切断結果のフラグメントを Native バッファから直接 `NativeMeshDataStore` へ追加登録する `AppendFragment`
  - 断面サブメッシュの再利用。既に断面を持つ破片を切り直してもサブメッシュ数が増えない
  - 設定は破片へ引き継がれるため、オブジェクト単位で混在できる
  - 追加登録で膨らんだストアを、生存中のオブジェクトが参照するぶんだけ残して自動再構築する仕組み
  - 切断済みのオブジェクトをプールへ返却する `MeshCutObjectPool.TryReleaseObject`

### Fixed

移植元から以下を修正しています。

- オブジェクト単位の Job に `innerloopBatchCount` として 32 を渡していたため、切断対象が 32 個未満のとき
  バッチが 1 つしか生成されず実質シングルスレッドで動作していた問題。ワーカースレッド数から自動算出するようにしました。
- `MeshData.SetSubMesh` に `DontRecalculateBounds` を渡したまま Bounds を設定しておらず、
  破片がフラスタムカリングで消える可能性があった問題。
- `MeshCutObjectPool` の非同期生成完了を待たずに破片を取得できてしまう問題。
  `WaitForGeneration()` を追加し、`MultiCutBlade.ExecuteCut` が待つようにしました。
- `MultiMeshCut.SetBatch` が 0 以下の値に警告を出しつつそのまま代入していた問題。
- 破片の要求数がプール生成数を超えたときに `IndexOutOfRangeException` になっていた問題。エラーlog を出して中断します。
- `CuttableObject.SetupCollider` がサンプリング点をワールド座標とみなして `worldToLocal` を掛けていたため、
  球コライダーがメッシュからオブジェクトの位置ぶんずれた場所に生成されていた問題。
  サンプリング点は元々メッシュローカル空間の座標なので、変換せずそのまま使うようにしました。
- k-means で 1 点も所属しなかったクラスタが、初期のランダム位置に半径 0 の球コライダーを残していた問題。無効化するようにしました。

### Changed

- 名前空間を `UsefulMeshCut` に統一しました(移植元でグローバル名前空間だった型を含む)。
- 処理時間の計測ログを `EnableProfileLog` で切り替えるようにしました(既定は無効)。
- 未使用だった `MultiMeshCut.LimitMs` と `MultiCutBlade` の未使用フィールドを削除しました。
- テスト実行用の属性を `[ContextMenu]` に置き換え、外部の属性ライブラリへの依存を解消しました。
- `CuttableObject.SetupCollider` と `MultiCutBlade.ApplyResult` から、使われていなかった `NativePlane` 引数を削除しました。
