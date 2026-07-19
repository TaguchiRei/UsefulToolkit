using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsefulToolkit.Framework
{
    public static class InstanceCollector
    {
        private static readonly Dictionary<Type, object> _cache = new Dictionary<Type, object>();

        /// <summary>
        /// 指定した型の派生クラスのインスタンスを取得する
        /// </summary>
        public static IReadOnlyList<T> GetInstances<T>(bool forceReload = false)
        {
            var typeOfT = typeof(T);
            if (!forceReload && _cache.TryGetValue(typeOfT, out var cachedInstances))
            {
                return (IReadOnlyList<T>)cachedInstances;
            }

            var types = TypeCollector.GetDerivedTypes<T>();
            var instances = new List<T>();
            foreach (var type in types)
            {
                try
                {
                    var instance = (T)Activator.CreateInstance(type);
                    if (instance is IInitializable initializable)
                    {
                        initializable.Initialize();
                    }
                    instances.Add(instance);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UsefulToolkit] Failed to create instance: {type.Name}\n{e}");
                }
            }

            _cache[typeOfT] = instances;
            return instances;
        }
    }
}