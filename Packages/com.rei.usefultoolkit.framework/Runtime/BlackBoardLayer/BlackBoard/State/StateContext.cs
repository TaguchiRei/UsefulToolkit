using System;
using UnityEngine;

namespace UsefulToolkit.Application.StateManagement
{
    /// <summary>
    /// Stateの変更時イベントのコンテキスト
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    public readonly struct StateContext<TValue>
    {
        /// <summary>
        /// 変更後の値
        /// </summary>
        public TValue NewValue { get; }

        /// <summary>
        /// 変更前の値
        /// </summary>
        public TValue OldValue { get; }

        public StateContext(TValue oldValue, TValue newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}