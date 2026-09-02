using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// キーごとにActionEntryを登録し、キーを指定してまとめて実行するリスト。
    /// キーに対応するリストは、そのキーへ初めてRegisterした時点で作られ、
    /// 登録が全て無くなった時点で取り除かれる。
    /// </summary>
    /// <typeparam name="TKey">Actionの登録先を指定するキー</typeparam>
    public sealed class KeyedActionEntryList<TKey>
    {
        private readonly Dictionary<TKey, ActionEntryList> _entryLists = new();

        /// <summary>
        /// 指定したキーへActionEntryを登録する。
        /// </summary>
        /// <param name="key">登録先のキー</param>
        /// <param name="entry">登録するActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable Register(TKey key, ActionEntry entry, string paramName)
        {
            if (_entryLists.TryGetValue(key, out var entryList))
            {
                entryList.Add(entry, paramName);
            }
            else
            {
                // Addが例外を投げた場合に空のリストだけが残らないよう、登録が通ってから辞書へ入れる
                entryList = new ActionEntryList();
                entryList.Add(entry, paramName);
                _entryLists[key] = entryList;
            }

            return new BoardDispose(() =>
            {
                if (!_entryLists.TryGetValue(key, out var target) || !target.Remove(entry)) return;

                RemoveIfEmpty(key);
            });
        }

        /// <summary>
        /// 登録できないActionEntryなら例外を投げる。キーに対応するリストの作成も含め、状態は変更しない。
        /// </summary>
        /// <param name="key">登録先のキー</param>
        /// <param name="entry">登録しようとしているActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public void ThrowIfCannotRegister(TKey key, ActionEntry entry, string paramName)
        {
            if (_entryLists.TryGetValue(key, out var entryList))
            {
                entryList.ThrowIfCannotRegister(entry, paramName);
                return;
            }

            ActionEntryList.ThrowIfNoAction(entry.HasAction, paramName);
        }

        /// <summary>
        /// 指定したキーへ登録されている全てのActionEntryを実行する。
        /// 登録が一つも無いキーを指定した場合は何もしない。
        /// </summary>
        /// <param name="key">実行するキー</param>
        public void Invoke(TKey key)
        {
            if (!_entryLists.TryGetValue(key, out var entryList)) return;

            entryList.Invoke();

            // DisposeOnUsedのActionEntryはInvokeの中で取り除かれる為、実行後に空になりうる
            RemoveIfEmpty(key);
        }

        /// <summary>
        /// 指定したキーに対応するリストが空なら、そのキーを辞書から取り除く。
        /// </summary>
        /// <param name="key">確認するキー</param>
        private void RemoveIfEmpty(TKey key)
        {
            // 実行や解除の最中に再登録されると辞書には別のリストが入っている為、
            // 取り出し直した現在のリストの件数で判断する
            if (_entryLists.TryGetValue(key, out var entryList) && entryList.Count == 0)
            {
                _entryLists.Remove(key);
            }
        }
    }
}
