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

## 使い方

シーンに置いた `CutBlade` (`MultiCutBlade`) を使う場合、インスペクタの右クリックメニュー「切断」で、
`BoxCollider` の範囲内にある `CuttableObject` をまとめて切断できます。スクリプトからは以下のように呼びます。

```csharp
using UsefulToolkit.MeshCut;

[SerializeField] private MultiCutBlade _blade;

private async void Cut(CuttableObject[] targets)
{
    await _blade.ExecuteCut(targets);
}
```

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

`MultiCutBlade` の「Enable Profile Log」を有効にすると、各処理段階の所要時間が Console に出力されます。
`MultiMeshCut` を直接使う場合は `EnableProfileLog` を `true` にしてください。

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
| `bool EnableProfileLog` | 処理時間ログの出力 |

### MultiCutBlade

自分自身の Transform を刃として扱います。`transform.position` が平面上の点、`transform.up` が法線です。
`ExecuteCut(CuttableObject[])` で切断からプールを使った破片への反映までを行います。

### CuttableObject

| メンバ | 説明 |
|---|---|
| `bool IsCuttable` | 現在切断できるか。切断済み、または1回だけ切断可能なオブジェクトの破片は false |
| `bool CanMultiCut` | 何回でも切断できる設定か |
| `int MeshId` | `NativeMeshDataStore` 上のメッシュID |
| `void SetRegisteredMesh(int)` | メッシュIDを設定し切断可能にする |
| `void DisableCutting()` | これ以上切断できない状態にする |
| `void InheritCutSettings(CuttableObject)` | 切断元から `CanMultiCut` を引き継ぐ |

切断対象および破片。`SetupCollider(List<Vector3>)` でサンプリング点の k-means クラスタリング結果から
球コライダーを配置します。`_colliderNum` は 7 以上である必要があります。

`MultiMeshCut.SamplingPoints` が返す点は**元オブジェクトのローカル空間**の座標です(切断はローカル空間で行われるため)。
破片の Transform は切断元と同一に設定されるので、`SetupCollider` はこれを変換せずそのまま
`SphereCollider.center` のローカル座標として扱います。自前で反映処理を書く場合はこの座標系に注意してください。

## 既知の制限

- 1 回の切断が完了するまでに最低 6 フレームかかります(処理段階ごとに Job の完了待ちを挟むため)。
- 中間バッファは最悪ケースの容量を毎回確保するため、頂点数の多いメッシュではメモリのスパイクが大きくなります。
