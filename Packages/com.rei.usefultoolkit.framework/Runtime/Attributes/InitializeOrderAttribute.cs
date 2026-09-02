using System;

namespace UsefulToolkit.Attributes
{
    /// <summary>
    /// InitializerBase派生クラスの初期化順を宣言する属性。
    /// GameCompositorGeneratorが生成するInjectAll・InitializeAllの呼び出し順に使われる。
    ///
    /// 値は昇順で、小さいものから先に初期化する。属性が付いていない型は0として扱う。
    /// 同じ値のものは型のフルネーム順になる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class InitializeOrderAttribute : Attribute
    {
        /// <summary> 初期化順。小さいものから先に初期化する </summary>
        public int Order { get; }

        public InitializeOrderAttribute(int order)
        {
            Order = order;
        }
    }
}
