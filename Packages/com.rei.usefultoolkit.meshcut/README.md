# Useful Toolkit - Mesh Cut

Burst + Job System で、複数のメッシュを一枚の刃(平面)で一括切断する Unity 向けライブラリです。

頂点の表裏判定・面の分類・面の切断・断面(キャップ)生成・コライダー用サンプリングまで、切断アルゴリズムの全工程が
`[BurstCompile]` された Job で実行されます。メインスレッドに残るのは Unity API が必須な処理
(Transform の読み取り、Mesh の生成、コライダーの配置)だけです。

## 動作要件

- Unity 6000.0 以降
- **UsefulToolkit - Framework** — `RecycleBuffer` / `IRecyclable` を使うため必須です
- **UniTask** — 別途インストールが必要です

UPM の `package.json` は git URL 依存を宣言できないため、UniTask は利用側で導入してください。
Package Manager の `Add package from git URL...` に以下を入力します。

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

Burst / Collections / Mathematics と Framework パッケージは、本パッケージの依存として自動的に解決されます。

## セットアップ

メニューから `UsefulToolkit > Mesh Cut > Setup` を開きます。

1. **シーンの準備** — 破片プレハブとプール生成数を指定して「シーンをセットアップ」を押すと、
   `MeshCut System` 配下に `MeshDataCache` / `FragmentPool` / `CutBlade` が生成され、相互参照が設定されます。
2. **オブジェクトの切断可能化** — Hierarchy で切断したいオブジェクトを選択し、断面マテリアル等を指定して
   「選択オブジェクトを切断可能化」を押すと `CuttableObject` が付与され、`MeshDataCache` の子へ移動します。
3. **状態** — 3 つのコンポーネントの配置状況と、`MeshDataCache` の子になっていない `CuttableObject` の数を確認できます。

### 破片プレハブについて

`CuttableObject` / `MeshFilter` / `Renderer` を持つプレハブを用意してください。物理を効かせる場合は `Rigidbody` も付けます。
球コライダーは `CuttableObject` が実行時に自動生成するため、あらかじめ付ける必要はありません。

### 重要な制約

切断対象は必ず `MeshDataCache` の子に配置してください。`MeshDataCache` は `Start()` で配下の
`CuttableObject` を走査してメッシュを登録し、`MeshId` を割り振ります。子でないオブジェクトは
`MeshId` が割り振られず、切断結果が壊れます。

## 1回だけ切断 / 何回でも切断

`CuttableObject` の **Can Multi Cut** で、そのオブジェクトを何回でも切れるようにするかを切り替えます。
この設定は破片へ引き継がれるため、オブジェクトごとに混在させられます。

| | Can Multi Cut = false | Can Multi Cut = true |
|---|---|---|
| 破片をもう一度切れるか | 切れない | 切れる |
| 切断結果のストア登録 | しない | する(実行時に追加登録) |
| 断面サブメッシュ | 1つ増える | 2回目以降は既存の断面サブメッシュへ追記 |

何回でも切断する場合、切断結果のメッシュデータが `MeshDataCache` のストアへ追加登録されます。
`Mesh` から読み直すのではなく切断Jobが書き出した Native バッファから直接コピーするため、
メインスレッドでの配列コピーは発生しません。

断面サブメッシュは**常に最後のサブメッシュ**です。既に断面を持つ破片を切り直したときは新しいサブメッシュを
足さずにそこへ追記するので、何度切ってもサブメッシュ数とドローコールは増えません。

### ストアの自動再構築

追加登録によってストアは切断のたびに伸びます。初期登録ぶんに対する増加が `MeshDataCache` の
**Rebuild Vertex Threshold**(既定 200,000 頂点)を超えると、次の切断の直前に、生存していて
まだ切断可能な `CuttableObject` が参照するメッシュだけを残してストアを作り直します。
手動で行いたい場合は `MeshDataCache.Rebuild()` を呼んでください。

### ストアを読む Job との関係

切断の Job はストアのデータを複製せず、直接読みます。ストアの `NativeList` は追加登録や再構築で中身の位置が変わるため、
`MeshDataCache` は読み取り中の Job を記録しておき(`AddStoreReader`)、ストアを変更・破棄する処理
(`Initialize` / `Rebuild` / `Unload` / 追加登録 / 破棄)の前に完了を待ちます(`CompleteStoreReaders`)。
自前でストアを読む Job を書く場合や、ストアを直接変更する場合も、この2つを使ってください。
別の刃の切断が進行中にストアを変更すると、その切断の Job が終わるまでメインスレッドが待ちます。

## 使い方

シーンに置いた `CutBlade` (`MultiCutBlade`) を使う場合、インスペクタの右クリックメニュー「切断」で、
`BoxCollider` の範囲内にある `CuttableObject` をまとめて切断できます(切断結果の組が Console に出力されます)。
スクリプトからは以下のように呼びます。

```csharp
using UsefulToolkit.MeshCut;

[SerializeField] private MultiCutBlade _blade;

private async void Cut(CuttableObject[] targets)
{
    MultiCutResult[] results = await _blade.ExecuteCut(targets);

    foreach (MultiCutResult result in results)
    {
        // result.Original : 切断した元の対象(非アクティブ、もう切れない)
        // result.Front    : 刃の法線(transform.up)の側の破片
        // result.Back     : 法線と反対の側の破片
    }
}
```

`ExecuteCut` は、すべての破片への反映(Transform・メッシュ・マテリアル・アクティブ化・球コライダー・切断設定の引き継ぎ・
物理の初速)が終わってから、実際に切断した対象ごとの組を返します。並び順は、渡した `targets` から切断できない対象
(null、`IsCuttable` が false)を除いた順です。何も切断しなかった場合(対象が空、すべて除外、破片の不足)は空の配列を返します。
戻り値が不要なら、これまでどおり `await _blade.ExecuteCut(targets);` と書けます。

#### 結果の参照の扱い

返された破片は、そのあとも使い続けられる保証はありません。プール(`MeshCutObjectPool`)は固定長のリングバッファで、
破片を取り出すたびに先頭を一周させるため、後の切断で同じ破片が回収され、別の破片として使い回されることがあります。
そのときは回収される破片の `ReuseAction` が呼ばれるので、結果を保持する場合はここで参照を手放してください。
元の対象がプールの破片だった場合(何回でも切断できる破片を切り直した場合)も、その対象はプールへ返されるので、同じように使い回されます。

切断処理だけを使い、破片への反映を自前で行う場合は `MultiMeshCut` を直接使います。

```csharp
using UsefulToolkit.MeshCut;

var slicer = new MultiMeshCut();

slicer.SetBatch(32);          // 頂点/三角形単位Jobの innerloopBatchCount (既定32)
slicer.SetSamplingCount(150); // コライダー用サンプリング点数 (既定150, 10以上)

NativePlane blade = new NativePlane(transform.position, transform.up);
await slicer.Cut(targets, blade);

// 結果は 対象数 × 2 個。i*2 が表(法線側)、i*2+1 が裏。
Mesh front = slicer.CutMesh[i * 2];
Mesh back = slicer.CutMesh[i * 2 + 1];
List<Vector3> points = slicer.SamplingPoints[i * 2]; // 元オブジェクトのローカル空間
```

反映処理の実装例は `MultiCutBlade.ApplyResult` を参照してください。**断面用のサブメッシュが 1 つ増える**ため、
Renderer のマテリアル配列の末尾に断面マテリアルを追加する必要があります。

### 処理時間の計測

`MultiCutBlade` の「Enable Profile Log」を有効にすると、切断のたびに全処理段階の計測結果が 1 つの表として Console に出力されます。
`MultiMeshCut` を直接使う場合は `EnableProfileLog` を `true` にしてください(その場合、表は切断処理のぶんだけになります)。

表の各行は 1 つの処理段階で、次の値を持ちます。

| 列 | 意味 |
|---|---|
| 種別 | `Main`(メインスレッド) / `Job`(ワーカースレッド) / `BG`(バックグラウンドスレッド) |
| 待ち | 実行を要求(Job のスケジュール・スレッド切り替え・待機開始)してから、実際に実行が始まるまで |
| 実行 | 実行していた時間。Job はワーカー上で最初に動き始めてから最後に終わるまで |
| 検知遅れ | 実行が終わってから、メインスレッドが完了に気付くまで |
| フレーム | 計測開始から数えた、その段階を検知したフレーム数 |

`└` で始まる行は直前の行の内訳で、合計には含みません。合計行の「何も実行していない時間」は、
経過時間からメインスレッドとワーカーの実行時間を引いたもので、フレームの切り替わり待ちなどに使われた時間です。

計測を有効にすると、Job の前後に時刻を記録するだけの Job が 1 つずつ挟まります。無効のときは何も挟まりません。
結果はコードからも `MultiCutBlade.LastProfile` / `MultiMeshCut.LastProfile` で取得できます。
Console に出さずに結果だけ取りたい場合は `MultiCutBlade.CollectProfile` を `true` にしてください。

## API

### MultiMeshCut

| メンバ | 説明 |
|---|---|
| `UniTask Cut(CuttableObject[], NativePlane)` | 切断を実行します |
| `bool Complete` | 切断が完了したか |
| `Mesh[] CutMesh` | 生成されたメッシュ。`i*2` が表、`i*2+1` が裏 |
| `List<List<Vector3>> SamplingPoints` | コライダー生成用のサンプリング点(元オブジェクトのローカル空間)。添字は `CutMesh` と同じ |
| `void SetBatch(int)` | 頂点/三角形単位Jobの `innerloopBatchCount`。オブジェクト単位のJobはワーカー数から自動算出されます |
| `void SetSamplingCount(int)` | サンプリング点数 |
| `bool EnableProfileLog` | 処理時間の計測と、表の Console 出力 |
| `MeshCutProfile LastProfile` | 最後に計測した切断の結果 |

### MultiCutBlade

自分自身の Transform を刃として扱います。`transform.position` が平面上の点、`transform.up` が法線です。
`ExecuteCut(CuttableObject[])` で切断からプールを使った破片への反映までを行います。

| メンバ | 説明 |
|---|---|
| `UniTask<MultiCutResult[]> ExecuteCut(CuttableObject[])` | 切断し、結果を破片へ反映します。反映が終わってから、切断した対象ごとの組を返します(何も切断しなければ空の配列) |
| `bool EnableProfileLog` | 処理時間の計測と、表の Console 出力(Inspector の「Enable Profile Log」と同じ) |
| `bool CollectProfile` | Console へ出さずに計測だけを行う |
| `MeshCutProfile LastProfile` | 最後に計測した `ExecuteCut` の結果。プール待ち・切断・破片反映の全段階を含みます |

### MultiCutResult

`MultiCutBlade.ExecuteCut` で 1 つの対象を切断した結果です(`readonly struct`)。参照の扱いは「結果の参照の扱い」を参照してください。

| メンバ | 説明 |
|---|---|
| `CuttableObject Original` | 切断した元の対象。切断後は非アクティブで、もう切れない |
| `CuttableObject Front` | 刃の法線(`transform.up`)の側の破片。`MultiMeshCut.CutMesh[i*2]` に当たる |
| `CuttableObject Back` | 刃の法線と反対の側の破片。`MultiMeshCut.CutMesh[i*2+1]` に当たる |

### MeshCutProfile

| メンバ | 説明 |
|---|---|
| `IReadOnlyList<MeshCutStageRecord> Stages` | 段階ごとの結果(名前・種別・待ち・実行・検知遅れ・フレーム) |
| `IReadOnlyList<MeshCutProfileInfo> Infos` | 対象数・頂点数・フラグメントバッファ確保量などの付帯情報 |
| `double ElapsedMs` / `int ElapsedFrames` | 全体の経過時間とフレーム数 |
| `double MainExecuteMs` / `WorkerExecuteMs` / `IdleMs` | メイン実行・ワーカー実行・何も実行していない時間 |
| `static MeshCutProfile Median(IReadOnlyList<MeshCutProfile>, string)` | 複数回の結果から各値の中央値をとります |
| `string ToString()` | Console 向けの表 |

### CuttableObject

| メンバ | 説明 |
|---|---|
| `bool IsCuttable` | 現在切断できるか。切断済み、または1回だけ切断可能なオブジェクトの破片は false |
| `bool CanMultiCut` | 何回でも切断できる設定か |
| `int MeshId` | `NativeMeshDataStore` 上のメッシュID |
| `void SetRegisteredMesh(int)` | メッシュIDを設定し切断可能にする |
| `void DisableCutting()` | これ以上切断できない状態にする |
| `void InheritCutSettings(CuttableObject)` | 切断元から `CanMultiCut` を引き継ぐ |
| `ColliderClusterSettings ColliderSettings` | 球コライダーを求めるときの設定値(球の数・縮小率・最大半径など) |
| `void SetupCollider(List<Vector3>)` | サンプリング点から球コライダーを求めて配置する(破片1つぶん) |
| `void ApplyColliderSpheres(NativeArray<float4>, int)` | `ColliderClusterJob` が求めた球をコライダーへ反映する |
| `void SetCutMesh(Mesh)` | 切断で生成されたメッシュを表示し、持ち主になる。以前に持っていたメッシュは破棄する |

`MultiMeshCut.CutMesh` のメッシュは切断のたびに新しく生成されます。自前で反映処理を書く場合は `SetCutMesh` で破片に渡すか、
不要になった時点で自分で `Destroy` してください(放置すると `Resources.UnloadUnusedAssets` まで解放されません)。
`SetCutMesh` には切断で生成したメッシュだけを渡してください。共有アセットを渡すと、差し替え時にそれごと破棄されます。

切断対象および破片。サンプリング点を k-means でクラスタリングした結果から球コライダーを配置します。
クラスタリングは Burst の `ColliderClusterJob` で行い、`MultiCutBlade` は全破片ぶんをまとめて並列に計算します。
`_colliderNum` は 7 以上である必要があります。

`MultiMeshCut.SamplingPoints` が返す点は**元オブジェクトのローカル空間**の座標です(切断はローカル空間で行われるため)。
破片の Transform は切断元と同一に設定されるので、`SetupCollider` はこれを変換せずそのまま
`SphereCollider.center` のローカル座標として扱います。自前で反映処理を書く場合はこの座標系に注意してください。

## 既知の制限

- 切断結果を受け取れるのは、切断を開始した次のフレームです(全 Job を 1 本の依存チェーンで実行し、完了を 1 回だけ待つため)。
- 切断のたびに、切断対象の頂点・三角形の合計に比例する中間バッファを確保します(切断対象の頂点・三角形データの複製と、
  破片用のバッファ)。破片用のバッファは実際に必要な量から求めますが、切断面付近の三角形については上限で見積もります。
