using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UsefulToolkit.External.Input;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 前回生成したときの、外部入力スロットのIdと名前の対応を読み書きする。
    /// 保存先は ProjectSettings/UsefulToolkitExternalInput.json。
    ///
    /// 次の生成時にこれと現在の宣言を突き合わせ、リネームと削除を検出する。
    /// </summary>
    internal static class ExternalInputManifest
    {
        private const string FilePath = "ProjectSettings/UsefulToolkitExternalInput.json";

        [Serializable]
        private sealed class Entry
        {
            public string id;
            public string name;
        }

        [Serializable]
        private sealed class Data
        {
            public List<Entry> slots = new();
        }

        /// <summary>
        /// 前回生成時の Id → 名前 を読み込む。ファイルが無い場合は空として成功する(初回生成)。
        /// 読み込めない場合はエラーを出してfalseを返す。
        /// </summary>
        /// <param name="previousNames">前回生成時の Id → 名前</param>
        /// <returns>読み込めた、またはファイルが無かった場合はtrue</returns>
        public static bool TryLoad(out Dictionary<string, string> previousNames)
        {
            previousNames = new Dictionary<string, string>();

            if (!File.Exists(FilePath)) return true;

            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));

                if (data?.slots == null) return true;

                foreach (var entry in data.slots)
                {
                    if (string.IsNullOrEmpty(entry?.id)) continue;

                    previousNames[entry.id] = entry.name;
                }

                return true;
            }
            catch (Exception exception)
            {
                // 読めないまま生成すると、リネームを見逃してバインディングが黙って壊れる為、生成ごと止める
                Debug.LogError(
                    $"[UsefulToolkit.Input] {FilePath} を読み込めない為、リネームを検出できず生成を中止しました。" +
                    $"ファイルを削除すると初回生成として扱います。\n{exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// 今回生成したスロットの Id → 名前 を保存する。
        /// </summary>
        /// <param name="slots">今回生成したスロット</param>
        public static void Save(IReadOnlyList<ExternalInputSlot> slots)
        {
            var data = new Data();

            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.Id)) continue;

                data.slots.Add(new Entry { id = slot.Id, name = slot.Name });
            }

            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        }
    }
}
