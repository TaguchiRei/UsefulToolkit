using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// ActionEntryを登録し、まとめて実行するリスト。
    /// 同じActionの二重登録は例外で弾く。
    /// 実行中に登録や解除が行われても、その回の実行対象は変わらない。
    /// DisposeOnUsedが立っているActionEntryは、実行される際にリストから取り除かれる。
    /// </summary>
    public sealed class ActionEntryList
    {
        private readonly List<ActionEntry> _entries = new();

        /// <summary> 登録されているActionEntryの数 </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// ActionEntryを登録する。
        /// </summary>
        /// <param name="entry">登録するActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable Register(ActionEntry entry, string paramName)
        {
            ThrowIfCannotRegister(entry, paramName);

            _entries.Add(entry);
            return new BoardDispose(() => _entries.Remove(entry));
        }

        /// <summary>
        /// 登録できないActionEntryなら例外を投げる。リストは変更しない。
        /// </summary>
        /// <param name="entry">登録しようとしているActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public void ThrowIfCannotRegister(ActionEntry entry, string paramName)
        {
            ThrowIfNoAction(entry.HasAction, paramName);

            if (_entries.Contains(entry))
            {
                throw new InvalidOperationException($"アクション [{entry.ActionName}] はすでに登録されています。");
            }
        }

        /// <summary>
        /// 登録されている全てのActionEntryを実行する。
        /// </summary>
        public void Invoke()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<ActionEntry> temporaryList = CollectionPool<List<ActionEntry>, ActionEntry>.Get();
            try
            {
                temporaryList.Clear();
                temporaryList.AddRange(_entries);

                // 実行前に取り除くことで、実行中に再びInvokeされても使い捨てのアクションは二度実行されない
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].DisposeOnUsed)
                    {
                        _entries.RemoveAt(i);
                    }
                }

                for (int i = 0; i < temporaryList.Count; i++)
                {
                    temporaryList[i].Invoke();
                }
            }
            finally
            {
                CollectionPool<List<ActionEntry>, ActionEntry>.Release(temporaryList);
            }
        }

        /// <summary>
        /// ActionEntryにActionが設定されていなければ例外を投げる。
        /// </summary>
        /// <param name="hasAction">ActionEntryにActionが設定されているか</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        internal static void ThrowIfNoAction(bool hasAction, string paramName)
        {
            if (!hasAction)
            {
                throw new ArgumentNullException(paramName, "ActionEntryに実行するActionが設定されていません。");
            }
        }
    }

    /// <summary>
    /// 引数を1つ取るActionEntryを登録し、まとめて実行するリスト。
    /// 振る舞いは<see cref="ActionEntryList"/>と同じ。
    /// </summary>
    public sealed class ActionEntryList<T>
    {
        private readonly List<ActionEntry<T>> _entries = new();

        /// <summary> 登録されているActionEntryの数 </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// ActionEntryを登録する。
        /// </summary>
        /// <param name="entry">登録するActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable Register(ActionEntry<T> entry, string paramName)
        {
            ThrowIfCannotRegister(entry, paramName);

            _entries.Add(entry);
            return new BoardDispose(() => _entries.Remove(entry));
        }

        /// <summary>
        /// 登録できないActionEntryなら例外を投げる。リストは変更しない。
        /// </summary>
        /// <param name="entry">登録しようとしているActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public void ThrowIfCannotRegister(ActionEntry<T> entry, string paramName)
        {
            ActionEntryList.ThrowIfNoAction(entry.HasAction, paramName);

            if (_entries.Contains(entry))
            {
                throw new InvalidOperationException($"アクション [{entry.ActionName}] はすでに登録されています。");
            }
        }

        /// <summary>
        /// 登録されている全てのActionEntryを実行する。
        /// </summary>
        /// <param name="value">引数</param>
        public void Invoke(T value)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<ActionEntry<T>> temporaryList = CollectionPool<List<ActionEntry<T>>, ActionEntry<T>>.Get();
            try
            {
                temporaryList.Clear();
                temporaryList.AddRange(_entries);

                // 実行前に取り除くことで、実行中に再びInvokeされても使い捨てのアクションは二度実行されない
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].DisposeOnUsed)
                    {
                        _entries.RemoveAt(i);
                    }
                }

                for (int i = 0; i < temporaryList.Count; i++)
                {
                    temporaryList[i].Invoke(value);
                }
            }
            finally
            {
                CollectionPool<List<ActionEntry<T>>, ActionEntry<T>>.Release(temporaryList);
            }
        }
    }

    /// <summary>
    /// 引数を2つ取るActionEntryを登録し、まとめて実行するリスト。
    /// 振る舞いは<see cref="ActionEntryList"/>と同じ。
    /// </summary>
    public sealed class ActionEntryList<T1, T2>
    {
        private readonly List<ActionEntry<T1, T2>> _entries = new();

        /// <summary> 登録されているActionEntryの数 </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// ActionEntryを登録する。
        /// </summary>
        /// <param name="entry">登録するActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <returns>Disposeすると登録を解除できる</returns>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public IDisposable Register(ActionEntry<T1, T2> entry, string paramName)
        {
            ThrowIfCannotRegister(entry, paramName);

            _entries.Add(entry);
            return new BoardDispose(() => _entries.Remove(entry));
        }

        /// <summary>
        /// 登録できないActionEntryなら例外を投げる。リストは変更しない。
        /// </summary>
        /// <param name="entry">登録しようとしているActionEntry</param>
        /// <param name="paramName">例外に含める引数名</param>
        /// <exception cref="ArgumentNullException">ActionEntryにActionが設定されていないときに出力</exception>
        /// <exception cref="InvalidOperationException">同じアクションエントリーが既に登録されているときに出力</exception>
        public void ThrowIfCannotRegister(ActionEntry<T1, T2> entry, string paramName)
        {
            ActionEntryList.ThrowIfNoAction(entry.HasAction, paramName);

            if (_entries.Contains(entry))
            {
                throw new InvalidOperationException($"アクション [{entry.ActionName}] はすでに登録されています。");
            }
        }

        /// <summary>
        /// 登録されている全てのActionEntryを実行する。
        /// </summary>
        /// <param name="value1">第一引数</param>
        /// <param name="value2">第二引数</param>
        public void Invoke(T1 value1, T2 value2)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            List<ActionEntry<T1, T2>> temporaryList =
                CollectionPool<List<ActionEntry<T1, T2>>, ActionEntry<T1, T2>>.Get();
            try
            {
                temporaryList.Clear();
                temporaryList.AddRange(_entries);

                // 実行前に取り除くことで、実行中に再びInvokeされても使い捨てのアクションは二度実行されない
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    if (_entries[i].DisposeOnUsed)
                    {
                        _entries.RemoveAt(i);
                    }
                }

                for (int i = 0; i < temporaryList.Count; i++)
                {
                    temporaryList[i].Invoke(value1, value2);
                }
            }
            finally
            {
                CollectionPool<List<ActionEntry<T1, T2>>, ActionEntry<T1, T2>>.Release(temporaryList);
            }
        }
    }
}
