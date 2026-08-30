using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// キーごとにActionEntryを登録し、キーを指定してまとめて実行するリスト。
    /// キーに対応するリストは、そのキーへ初めてRegisterした時点で作られる。
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
            if (!_entryLists.TryGetValue(key, out var entryList))
            {
                entryList = new ActionEntryList();
                _entryLists[key] = entryList;
            }

            return entryList.Register(entry, paramName);
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
            if (_entryLists.TryGetValue(key, out var entryList))
            {
                entryList.Invoke();
            }
        }
    }
}
