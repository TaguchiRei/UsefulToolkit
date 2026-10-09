---
name: uloop-export-vfx-graph
toolName: export-vfx-graph
description: "Export a Unity VFX Graph asset (.vfx / .vfxoperator / .vfxblock) as compact Markdown: systems, contexts, blocks, operators, properties, links and values. Use to read, explain, review or compare a visual effect instead of reading the raw .vfx YAML."
---

# uloop export-vfx-graph

VFX Graph のアセットを、AI が読める Markdown にして返す。生の `.vfx` は YAML で、スロット 1 つごとに別オブジェクトになり `fileID` で参照し合うため、小さなエフェクトでも数千行になる。このツールは構造と値だけを残す（例: 8,900 行の `.vfx` が 140 行程度になる）。

エフェクトの内容を説明する、レビューする、2 つのエフェクトを比べるときは、`.vfx` を直接読まずにこれを使う。

## 使い方

```bash
uloop export-vfx-graph --asset-path Assets/Effects/Sparks.vfx
```

- `--asset-path`: `Assets/` または `Packages/` から始まるプロジェクト相対パス。`.vfx` のほか、Subgraph の `.vfxoperator` / `.vfxblock` も読める
- Unity Editor がこのプロジェクトで起動している必要がある。インポート済みのアセットしか読めない

## 出力

JSON の `Markdown` に本文が入る。失敗時は `Success` が false になり、`ErrorMessage` に理由が入る。

```json
{ "AssetPath": "...", "Markdown": "# VFX Graph: ...", "ErrorMessage": "", "Success": true }
```

本文の構成:

- `## Properties`: Blackboard のプロパティ。`p1` から順に ID が振られる。`exposed` は Inspector に公開されているもの、`output` は Subgraph の出力
- `## Systems`: 同じパーティクルデータを共有する Context のまとまり。各 Context（`c1` …）に Flow の接続先（`-> c2`）、Settings、入力スロット、Block（`c2.b1` …）が並ぶ
- `## Operators`: Operator（`o1` …）。Settings と入力スロット
- `## Notes`: 付箋とグループ

入力スロットの書き方:

- `name = value`: 値。`*` はデフォルトから変更された値、`[World]` / `[Local]` は座標空間
- `name <- o3.r`: 接続。`o3` の出力 `r` から来ている。出力が 1 つの Operator や Property は ID だけになる
- `name = value; .x <- p2`: 子スロット単位の接続（例: Position の x だけ）
- Block の `[disabled]` は無効化された Block

Settings の名前と値は VFX Graph 内部のフィールド名・enum 名のまま出る（例: `blendMode=Additive`, `m_Subgraph=<path>`）。

## コツ

- Subgraph を使っているノードは `m_Subgraph=` にアセットパスが出るので、そのパスをもう一度このツールに渡すと中身を読める
- 2 つのエフェクトを比べるときは、両方を書き出してから比べる
- Git の過去のコミットにある `.vfx` は読めない（インポートされていないため）。比べたいときは、そのバージョンをプロジェクト内に置いてインポートさせる
- 書き出すだけで、アセットは変更しない
