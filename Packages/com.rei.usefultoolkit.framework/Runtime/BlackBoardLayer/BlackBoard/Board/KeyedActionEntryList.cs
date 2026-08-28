using System;
using System.Collections.Generic;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// キーごとに<see cref="ActionEntryList"/>を保持するリスト。
    /// 「特定のシーンがロードされたとき」のように、対象を指定して登録するActionへ使う。
    /// リストはRegisterで初めて必要になった時点で作られる。
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
        /// 登録できないActionEntryを弾く。
        /// 登録の前に別の処理を挟む場合に、Registerと分けて先に呼ぶ。
        /// このメソッドではリストを作らないため、結局登録しなかった場合に空のリストが残らない。
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

            // リストが無ければ重複はあり得ないため、Actionが設定されているかだけを見る
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
