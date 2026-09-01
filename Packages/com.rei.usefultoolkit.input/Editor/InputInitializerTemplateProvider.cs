using System.Collections.Generic;
using System.Text;
using UsefulToolkit.Editor.Initialize;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// 常駐シーンの生成時に、利用者のアセンブリへ <c>InputInitializer</c> を生成する
    /// <see cref="IInitializerTemplateProvider"/> 実装。
    ///
    /// 入力の操作面を DI コンテナへ登録するには生成された Compositor の具象型が要るため、
    /// パッケージ側の <c>InputInitializerBase</c> を継承したクラスを利用者側に置く。
    /// (map, action) の Bind も利用者の enum に依存するため、その置き場を兼ねている。
    /// </summary>
    internal sealed class InputInitializerTemplateProvider : IInitializerTemplateProvider
    {
        private const string ClassName = "InputInitializer";

        public int Order => 0;

        public IEnumerable<InitializerTemplate> GetTemplates(InitializerTemplateContext context)
        {
            yield return new InitializerTemplate(ClassName, BuildSource(context));
        }

        /// <summary>
        /// 生成する InputInitializer のソースを組み立てる。
        /// </summary>
        /// <param name="context">生成先の名前空間と Compositor クラス名</param>
        private static string BuildSource(InitializerTemplateContext context)
        {
            var builder = new StringBuilder();

            builder.AppendLine("// UsefulToolkit が生成した Initializer のテンプレートです。");
            builder.AppendLine("// 中身は自由に書き換えられます。再生成時、このファイルが既に存在する場合は上書きされません。");
            builder.AppendLine($"// 生成元シーン : {context.SceneName}");
            builder.AppendLine();
            builder.AppendLine("using UsefulToolkit.BlackBoard.BlackBoard;");
            builder.AppendLine("using UsefulToolkit.Initialization;");
            builder.AppendLine();
            builder.AppendLine($"namespace {context.NamespaceName}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>入力システムの初期化と、(map, action) の橋渡しを行う。</summary>");
            builder.AppendLine($"    public sealed class {ClassName} : InputInitializerBase");
            builder.AppendLine("    {");
            builder.AppendLine("        private void Awake()");
            builder.AppendLine("        {");
            builder.AppendLine("            // 入力の操作面をDIコンテナへ登録する。BlackBoardには載せない。");
            builder.AppendLine("            // 受け取る側は IInjectable<UsefulToolkit.BlackBoard.Input.IInputController> を実装する。");
            builder.AppendLine($"            {context.CompositorClassName}.TryRegisterContent(Controller);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        /// <param name=\"blackBoard\">InputStateの登録先</param>");
            builder.AppendLine("        public override void Initialize(IBlackBoard blackBoard)");
            builder.AppendLine("        {");
            builder.AppendLine("            base.Initialize(blackBoard);");
            builder.AppendLine();
            builder.AppendLine("            // ここに (map, action) の Bind と、有効にする ActionMap の指定を書く。");
            builder.AppendLine("            // Bind は InputActionAsset の Action を入力ソースとしてチャンネルへ繋ぐ操作で、");
            builder.AppendLine("            // UsefulToolkit/Input/Generate Action Enums が生成した enum を使う。");
            builder.AppendLine("            //");
            builder.AppendLine("            // 例:");
            builder.AppendLine("            // Controller.Bind<UnityEngine.Vector2>(ActionMaps.Player, PlayerActions.Move);");
            builder.AppendLine("            // Controller.SwitchActionMap(ActionMaps.Player);");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }
    }
}
