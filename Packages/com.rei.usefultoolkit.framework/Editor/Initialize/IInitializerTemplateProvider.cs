using System.Collections.Generic;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// 常駐シーンの生成時に、利用者のアセンブリへ配置するInitializerのソースを提供する拡張点。
    ///
    /// <see cref="UsefulToolkit.Initialization.GameCompositor{TSelf}.TryRegisterContent{T}"/> は
    /// 生成されたCompositorの具象型を通してしか呼べず、その具象型はパッケージ側のアセンブリからは
    /// 参照できない。そのためパッケージは実処理を抽象クラスへ置き、その派生クラスのソースを
    /// ここで提供して利用者のアセンブリ側に生成させる。
    ///
    /// 発見対象になるには、引数なしのコンストラクタを持つ具象クラスであること。
    /// 生成先に同名のファイルが既に存在する場合は、警告を出して生成を行わない。
    /// </summary>
    public interface IInitializerTemplateProvider
    {
        /// <summary>呼び出し順。小さいほど先に呼ばれる。</summary>
        int Order { get; }

        /// <summary>
        /// 生成するInitializerのソースを列挙する。
        /// </summary>
        /// <param name="context">生成先の名前空間とCompositorクラス名</param>
        IEnumerable<InitializerTemplate> GetTemplates(InitializerTemplateContext context);
    }
}
