using System;
using System.Collections.Generic;

namespace UsefulToolkit.Initialization
{
    /// <summary>
    /// Compositor が収集した依存を、Compositor の具象型ごとのスコープに分けて保持する。
    ///
    /// 解決は「呼び出し元自身のスコープ → Root スコープ」の 2 段のみで、
    /// シーン同士がお互いのスコープを覗くことはできない。Root は常駐シーンとして
    /// 先に存在することが保証されている唯一のスコープなので、フォールバック先はここに限定する。
    ///
    /// このクラスは Initialization アセンブリの internal であり、
    /// 利用側アセンブリの Compositor 派生クラスからは直接触れない。
    /// </summary>
    internal sealed class CompositionContainer
    {
        /// <summary>登録の結果。重複時に呼び出し側がエラー文言を切り替えるため、種類を分けて返す。</summary>
        internal enum AddResult
        {
            Success,
            DuplicateInOwnScope,
            DuplicateInRootScope
        }

        private readonly Dictionary<Type, Dictionary<Type, object>> _scopes = new();

        private Type _rootScope;

        /// <summary>指定スコープを Root スコープにする。既に別のスコープが Root なら false を返す。</summary>
        internal bool TrySetRootScope(Type scope)
        {
            if (_rootScope != null && _rootScope != scope) return false;

            _rootScope = scope;
            return true;
        }

        /// <summary>Root スコープの指定を解除する。設定した本人以外からの解除は無視する。</summary>
        internal void ClearRootScope(Type scope)
        {
            if (_rootScope == scope) _rootScope = null;
        }

        /// <summary>
        /// 依存を登録する。自分のスコープと Root スコープのどちらかに同じ型が既にあれば失敗する。
        /// Root にある型をシーン側が上書きすることも許さない。同じ型の実体が複数の経路で配られると、
        /// 参照が分裂してどれが正本か分からなくなる為。
        /// </summary>
        internal AddResult TryAdd(Type scope, Type key, object instance)
        {
            if (_scopes.TryGetValue(scope, out var own) && own.ContainsKey(key))
            {
                return AddResult.DuplicateInOwnScope;
            }

            if (_rootScope != null
                && _rootScope != scope
                && _scopes.TryGetValue(_rootScope, out var root)
                && root.ContainsKey(key))
            {
                return AddResult.DuplicateInRootScope;
            }

            if (own == null)
            {
                own = new Dictionary<Type, object>();
                _scopes[scope] = own;
            }

            own.Add(key, instance);
            return AddResult.Success;
        }

        /// <summary>自分のスコープ、次に Root スコープの順で依存を探す。</summary>
        internal bool TryGet<T>(Type scope, out T instance)
        {
            if (TryGetFromScope(scope, out instance)) return true;

            if (_rootScope != null && _rootScope != scope && TryGetFromScope(_rootScope, out instance))
            {
                return true;
            }

            instance = default;
            return false;
        }

        /// <summary>指定スコープの登録内容を全て破棄する。Compositor の破棄時に呼ぶ。</summary>
        internal void ClearScope(Type scope)
        {
            _scopes.Remove(scope);
        }

        private bool TryGetFromScope<T>(Type scope, out T instance)
        {
            if (_scopes.TryGetValue(scope, out var entries)
                && entries.TryGetValue(typeof(T), out var raw)
                && raw is T typed)
            {
                instance = typed;
                return true;
            }

            instance = default;
            return false;
        }
    }
}
