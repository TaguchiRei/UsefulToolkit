using System;
using System.Collections.Generic;

namespace UsefulToolkit.Framework
{
    public abstract class SceneGroupBase<T> where T : Enum
    {
        /// <summary>
        /// このグループが読み込みを必要とするシーンの一覧。
        /// </summary>
        public abstract IReadOnlyList<T> Scenes { get; }
    }
}