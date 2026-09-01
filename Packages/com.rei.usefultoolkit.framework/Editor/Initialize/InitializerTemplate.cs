namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// 生成するInitializerスクリプト1つ分の内容。
    /// </summary>
    public readonly struct InitializerTemplate
    {
        /// <summary>生成するクラス名。ファイル名は「クラス名.cs」になる。</summary>
        public readonly string ClassName;

        /// <summary>ファイルへ書き出すソース全文。</summary>
        public readonly string Source;

        /// <param name="className">生成するクラス名</param>
        /// <param name="source">ファイルへ書き出すソース全文</param>
        public InitializerTemplate(string className, string source)
        {
            ClassName = className;
            Source = source;
        }
    }

    /// <summary>
    /// テンプレートの組み立てに必要な、生成先の情報。
    /// </summary>
    public readonly struct InitializerTemplateContext
    {
        /// <summary>生成先の名前空間。生成されるCompositorと同じものが渡る。</summary>
        public readonly string NamespaceName;

        /// <summary>
        /// 常駐シーンのCompositorクラス名。
        /// TryRegisterContentはこの具象型の静的メソッドとして呼ぶ必要があるため、
        /// テンプレート側はこの名前を使って呼び出しを書く。
        /// </summary>
        public readonly string CompositorClassName;

        /// <summary>常駐シーンのシーン名。</summary>
        public readonly string SceneName;

        /// <param name="namespaceName">生成先の名前空間</param>
        /// <param name="compositorClassName">常駐シーンのCompositorクラス名</param>
        /// <param name="sceneName">常駐シーンのシーン名</param>
        public InitializerTemplateContext(string namespaceName, string compositorClassName, string sceneName)
        {
            NamespaceName = namespaceName;
            CompositorClassName = compositorClassName;
            SceneName = sceneName;
        }
    }
}
