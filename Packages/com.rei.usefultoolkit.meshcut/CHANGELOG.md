# Changelog

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
