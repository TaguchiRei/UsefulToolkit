---
name: uloop-vfx-graph
description: "Read and edit Unity VFX Graph assets (.vfx / .vfxoperator / .vfxblock) as text: export a graph to compact Markdown, apply a JSON list of edits (set values and settings, add/remove blocks, operators, properties, link slots and flows), and search addable nodes. Use to explain, review, compare or modify a visual effect instead of reading or writing the raw .vfx YAML."
---

# uloop vfx-graph

VFX Graph を、構造と値だけを残した Markdown で読み、JSON の操作リストで編集する。生の `.vfx` は YAML で、スロット 1 つごとに別オブジェクトになり `fileID` で参照し合うため、小さなエフェクトでも数千行になる。直接読み書きせずに、このツールを使う。

編集はグラフ画面のコントローラーを通す。人がグラフ画面で操作したときと同じ処理になり、Undo（Ctrl+Z）で戻せる。

## 基本の流れ

1. `uloop export-vfx-graph --asset-path Assets/Effects/Sparks.vfx` で書き出し、`Markdown` と `Revision` を受け取る
2. Markdown の ID（`c2.b1`、`o3`、`p1`）を使って操作リストの JSON ファイルを書く
3. `uloop edit-vfx-graph --asset-path Assets/Effects/Sparks.vfx --revision <Revision> --operations-file ops.json` で適用する
4. 返ってきた `Markdown` で結果を確かめる。`Warnings` にコンパイルの失敗などが入っていたら `uloop get-logs` で原因を見る。続けて編集するときは、返ってきた新しい `Revision` を使う

- Unity Editor がこのプロジェクトで起動している必要がある。インポート済みのアセットしか読めない
- 編集すると、対象アセットのグラフ画面が Unity の中で開く（開いていなければ開く）
- `--save` を付けなければ保存しない。変更は未保存のままグラフ画面に残り、人が見て保存（Ctrl+S）するか、Undo で戻す。自分でプレイして確かめるなど保存が必要なときだけ `--save` を付ける
- 元に戻す手段は Undo と Git だけ。大きな編集の前は、ユーザーにコミットを勧める
- コンパイル（ドメインリロード）を挟むと、グラフ画面の Undo の記録がリセットされ、それより前の編集は Undo で戻せなくなる（VFX Graph の仕様）
- `Revision` はノードの並び（ID）だけのハッシュ。値や設定が変わっても変わらない

## Parameters

### export-vfx-graph

Export a VFX Graph asset as compact Markdown with node IDs and a Revision for edit-vfx-graph

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--asset-path` | string | - | Project-relative path of the VFX Graph asset (.vfx / .vfxoperator / .vfxblock), starting with Assets/ or Packages/ |

### edit-vfx-graph

Apply a JSON list of operations to a VFX Graph through its graph window, as one undoable step. All operations are applied or none are

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--asset-path` | string | - | Project-relative path of the VFX Graph asset to edit, starting with Assets/ or Packages/ |
| `--revision` | string | - | Revision returned by export-vfx-graph. The edit is rejected if the graph has changed since then |
| `--operations-file` | string | - | Path of a JSON file holding the operations (an array, or an object with an "operations" array). Absolute or project-relative |
| `--save` | flag | - | Save the asset after applying. Without it the changes stay unsaved in the open graph window for review |

### list-vfx-nodes

Search the blocks, operators, contexts and property types that edit-vfx-graph can add

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `--kind` | enum | `Block` | Kind of node to list: Block, Operator, Context or Property |
| `--query` | string | - | Words that must all appear in the name, category or type name (case-insensitive). Empty lists everything |
| `--max-count` | integer | `50` | Maximum number of entries to return |

## 書き出しの読み方

- `## Properties`: Blackboard のプロパティ（`p1` …）。`exposed` は Inspector に公開、`output` は Subgraph の出力
- `## Systems`: 同じパーティクルデータを共有する Context のまとまり。Context（`c1` …）ごとに Flow の接続先（`-> c2`）、Settings、入力スロット、Block（`c2.b1` …）が並ぶ
- `## Operators`: Operator（`o1` …）
- `## Notes`: 付箋とグループ
- 入力スロット: `name = value` は値で、`*` はデフォルトから変更された値、`[World]` / `[Local]` は座標空間。`name <- o3.r` は `o3` の出力 `r` からの接続。`name = value; .x <- p2` は子スロット単位の接続
- Block の `[disabled]` は無効化された Block
- Settings は VFX Graph 内部のフィールド名・enum 名のまま出る（`blendMode=Additive`、`m_Subgraph=<path>` など）。Subgraph はそのパスをもう一度書き出すと中身を読める

## 操作リスト

```json
[
  { "op": "setInput", "target": "c2.b1", "slot": "A", "value": 2 },
  { "op": "addBlock", "context": "c3", "type": "Turbulence", "at": 2, "as": "turb" },
  { "op": "setInput", "target": "$turb", "slot": "Intensity", "value": 3 },
  { "op": "addOperator", "type": "Random Float", "as": "rnd" },
  { "op": "link", "from": "$rnd", "to": "c2.b1", "toSlot": "B" }
]
```

- ID は `--revision` を書き出した時点のグラフを指す。途中で Block を追加しても、既存の `c3.b2` は書き出したときの `c3.b2` のまま
- 追加したノードは `"as": "name"` で別名を付け、後の操作で `"$name"` と書いて参照する。適用後の ID は応答の `Aliases` に入る
- 1 つでも失敗すると全体を取り消し、`ErrorMessage` に何番目の操作がなぜ失敗したか（指定できるスロット名・設定名の一覧など）が入る

| op | 引数 | 内容 |
|----|------|------|
| `setInput` | `target`, `slot`, `value` | 入力スロットの値。`slot` は `A`、`arcSphere.sphere.radius` のように主スロットから `.` でつなぐ。接続されているスロットは変更できない |
| `setSetting` | `target`, `setting`, `value` | Settings の値（書き出しの名前のまま） |
| `setBlockEnabled` | `target`, `enabled` | Block の有効・無効 |
| `setProperty` | `target`, `value`?, `name`?, `exposed`? | Property の値・名前・公開 |
| `link` | `from`, `fromSlot`?, `to`, `toSlot` | 出力から入力へ接続。出力が 1 つの Operator と Property は `fromSlot` を省略できる。Property の子は `p1` + `x` のように主スロット名を省く |
| `unlink` | `to`, `toSlot` | その入力スロットへの接続をすべて外す |
| `linkFlow` / `unlinkFlow` | `from`, `to`, `fromSlot`?, `toSlot`? | Context 同士の Flow。スロット番号は省略時 0（書き出しの `#1` が 1） |
| `remove` | `target` | Context・Block・Operator・Property を削除 |
| `setPosition` | `target`, `position` | Context・Operator・Property のグラフ上の位置（`[x, y]`）。Block は Context の中に並ぶので指定できない |
| `addBlock` | `context`, `type`, `category`?, `at`?, `settings`?, `as`? | Block を追加。`at` は追加後の番号（`c3.b2` の 2）、省略時は末尾 |
| `addOperator` / `addContext` | `type`, `category`?, `position`?, `settings`?, `as`? | ノードを追加。位置は省略時、既存ノードの横に並ぶ |
| `addProperty` | `type`, `name`?, `exposed`?, `value`?, `as`? | Property を追加。`type` は `Float`、`Vector 3`、`Gradient` など |

`type` には `list-vfx-nodes` の `Name` を渡す（大文字小文字は区別しない）。同じ名前が複数あるときは `category` に `Category` の一部を渡して絞る。

値の書き方:

- 数値・真偽値・文字列はそのまま。enum は名前（`"Additive"`）
- Vector は `[x, y, z]`、色は `"#RRGGBB"` / `"#RRGGBBAA"` / `[r, g, b, a]`
- Position や AABox などの VFX の型は `{"center": [0, 1, 0], "size": [2, 2, 2]}` のようにフィールドで書く。書かなかったフィールドは今の値のまま。フィールドが 1 つの型（Position、Direction、Vector）は値を直接書ける
- `Set ... Over Life` は合成方法が Overwrite で、初期値を上書きする。初期値に掛けたいときは `Multiply ... Over Life` を使う
- Gradient は `{"mode": "Blend", "colors": [{"time": 0, "color": "#FFFFFF"}], "alphas": [{"time": 0, "alpha": 1}]}`
- AnimationCurve は `[[0, 0], [1, 1]]` か `{"keys": [{"time": 0, "value": 0, "inTangent": 0, "outTangent": 0}], "postWrapMode": "Loop"}`
- テクスチャなどのアセットはパス（`"Assets/Textures/Spark.png"`）、外すときは `""`

## コツ

- 2 つのエフェクトを比べるときは、両方を書き出してから比べる
- Git の過去のコミットにある `.vfx` は読めない（インポートされていないため）
- 操作リストのファイルは、`.uloop/outputs/` などプロジェクトに含まれない場所に置く
