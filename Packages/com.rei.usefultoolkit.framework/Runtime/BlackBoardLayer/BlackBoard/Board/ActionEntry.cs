using System;

namespace UsefulToolkit.BlackBoard.BlackBoard
{
    /// <summary>
    /// 使用後にActionを無効化するかどうかを保持したActionの構造体
    /// </summary>
    public readonly struct ActionEntry : IEquatable<ActionEntry>
    {
        public readonly bool DisposeOnUsed;
        private readonly Action _action;

        /// <summary> 実行するActionが設定されているか </summary>
        public bool HasAction => _action != null;

        /// <summary> ログ表示用のAction名。未設定なら"null" </summary>
        public string ActionName => _action?.Method.Name ?? "null";

        /// <summary>
        /// ActionがNullでなければ実行する
        /// </summary>
        public readonly void Invoke()
        {
            _action?.Invoke();
        }

        public ActionEntry(bool disposeOnUsed, Action action)
        {
            DisposeOnUsed = disposeOnUsed;
            _action = action;
        }

        /// <summary>
        /// 同一性はActionのみで判定する。
        /// DisposeOnUsedの違いで別物として扱うと、同じActionが二重登録されて二重実行になるため。
        /// </summary>
        public bool Equals(ActionEntry other)
        {
            return Equals(_action, other._action);
        }

        public override bool Equals(object obj)
        {
            return obj is ActionEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _action?.GetHashCode() ?? 0;
        }
    }

    public readonly struct ActionEntry<T> : IEquatable<ActionEntry<T>>
    {
        public readonly bool DisposeOnUsed;
        private readonly Action<T> _action;

        /// <summary> 実行するActionが設定されているか </summary>
        public bool HasAction => _action != null;

        /// <summary> ログ表示用のAction名。未設定なら"null" </summary>
        public string ActionName => _action?.Method.Name ?? "null";

        /// <summary>
        /// ActionがNullでなければ実行する
        /// </summary>
        public readonly void Invoke(T value)
        {
            _action?.Invoke(value);
        }

        public ActionEntry(bool disposeOnUsed, Action<T> action)
        {
            DisposeOnUsed = disposeOnUsed;
            _action = action;
        }

        /// <summary>
        /// 同一性はActionのみで判定する。
        /// DisposeOnUsedの違いで別物として扱うと、同じActionが二重登録されて二重実行になるため。
        /// </summary>
        public bool Equals(ActionEntry<T> other)
        {
            return Equals(_action, other._action);
        }

        public override bool Equals(object obj)
        {
            return obj is ActionEntry<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _action?.GetHashCode() ?? 0;
        }
    }

    public readonly struct ActionEntry<T1, T2> : IEquatable<ActionEntry<T1, T2>>
    {
        public readonly bool DisposeOnUsed;
        private readonly Action<T1, T2> _action;

        /// <summary> 実行するActionが設定されているか </summary>
        public bool HasAction => _action != null;

        /// <summary> ログ表示用のAction名。未設定なら"null" </summary>
        public string ActionName => _action?.Method.Name ?? "null";

        /// <summary>
        /// ActionがNullでなければ実行する
        /// </summary>
        public readonly void Invoke(T1 value, T2 value2)
        {
            _action?.Invoke(value, value2);
        }

        public ActionEntry(bool disposeOnUsed, Action<T1, T2> action)
        {
            DisposeOnUsed = disposeOnUsed;
            _action = action;
        }

        /// <summary>
        /// 同一性はActionのみで判定する。
        /// DisposeOnUsedの違いで別物として扱うと、同じActionが二重登録されて二重実行になるため。
        /// </summary>
        public bool Equals(ActionEntry<T1, T2> other)
        {
            return Equals(_action, other._action);
        }

        public override bool Equals(object obj)
        {
            return obj is ActionEntry<T1, T2> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _action?.GetHashCode() ?? 0;
        }
    }
}