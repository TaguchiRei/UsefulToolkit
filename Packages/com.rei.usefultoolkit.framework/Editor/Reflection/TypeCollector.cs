using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace UsefulToolkit.Framework
{
    public static class TypeCollector
    {
        /// <summary>
        /// 指定した型の派生クラス（非抽象）を取得する
        /// </summary>
        public static IReadOnlyList<Type> GetDerivedTypes<T>()
        {
            return TypeCache.GetTypesDerivedFrom<T>()
                .Where(t => !t.IsAbstract)
                .ToList();
        }
    }
}