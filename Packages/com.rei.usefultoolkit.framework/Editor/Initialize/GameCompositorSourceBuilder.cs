using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UsefulToolkit.Initialization;

namespace UsefulToolkit.Editor.Initialize
{
    /// <summary>
    /// シーンから収集した型情報をもとに、GameCompositorの派生クラスのソースを組み立てる。
    /// 型の探索・IInjectableの判定はすべてここ(Editor)で完結させ、生成後のコードには
    /// リフレクションを一切残さない。
    /// </summary>
    internal static class GameCompositorSourceBuilder
    {
        /// <summary>IInjectableの引数違いの定義。実装判定のキーに使う。</summary>
        private static readonly Type[] InjectableDefinitions =
        {
            typeof(IInjectable<>),
            typeof(IInjectable<,>),
            typeof(IInjectable<,,>),
            typeof(IInjectable<,,,>)
        };

        /// <summary>シーン内で見つかったInitializerの型と、その個体数から決まるフィールド情報。</summary>
        internal readonly struct InitializerField
        {
            public readonly Type InitializerType;
            public readonly string FieldName;

            /// <summary>同じ型が複数個体あるならList、1つならそのまま参照する。</summary>
            public readonly bool IsList;

            public InitializerField(Type initializerType, string fieldName, bool isList)
            {
                InitializerType = initializerType;
                FieldName = fieldName;
                IsList = isList;
            }
        }

        /// <param name="isRoot">
        /// 常駐シーン用の Root Compositor として生成するか。true なら <see cref="RootGameCompositor{TSelf}"/> を
        /// 継承して ChildBoard の登録も出力する。false なら <see cref="GameCompositor{TSelf}"/> 継承で
        /// Inject / Initialize のみを出力し、ボード配列は無視する。
        /// </param>
        public static string Build(
            string namespaceName,
            string className,
            string sceneName,
            IReadOnlyList<Type> stateBoardTypes,
            IReadOnlyList<Type> eventBoardTypes,
            IReadOnlyList<InitializerField> initializerFields,
            bool isRoot)
        {
            var builder = new StringBuilder();

            string selfType = string.IsNullOrEmpty(namespaceName)
                ? className
                : $"{namespaceName}.{className}";

            string initializationNamespace = typeof(GameCompositor<>).Namespace;
            string baseTypeName = isRoot
                ? $"{initializationNamespace}.RootGameCompositor<{selfType}>"
                : $"{initializationNamespace}.GameCompositor<{selfType}>";

            builder.AppendLine("// 自動生成ファイルの為、手動での編集は上書きされます。");
            builder.AppendLine($"// 生成元シーン : {sceneName}");
            builder.AppendLine();
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine();
            builder.AppendLine($"namespace {namespaceName}");
            builder.AppendLine("{");
            builder.AppendLine($"    /// <summary>{sceneName} の合成ルート。</summary>");
            builder.AppendLine(
                $"    public sealed class {className} : {baseTypeName}");
            builder.AppendLine("    {");

            AppendFields(builder, initializerFields);

            if (isRoot)
            {
                AppendRegisterChildBoards(builder, stateBoardTypes, eventBoardTypes);
            }

            AppendInjectAll(builder, initializerFields);
            AppendInitializeAll(builder, initializerFields);

            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static void AppendFields(StringBuilder builder, IReadOnlyList<InitializerField> fields)
        {
            if (fields.Count == 0) return;

            foreach (var field in fields)
            {
                string typeName = TypeName(field.InitializerType);
                string declaredType = field.IsList
                    ? $"System.Collections.Generic.List<{typeName}>"
                    : typeName;
                string initializer = field.IsList ? " = new()" : string.Empty;

                builder.AppendLine($"        [SerializeField] private {declaredType} {field.FieldName}{initializer};");
            }

            builder.AppendLine();
        }

        private static void AppendRegisterChildBoards(
            StringBuilder builder,
            IReadOnlyList<Type> stateBoardTypes,
            IReadOnlyList<Type> eventBoardTypes)
        {
            builder.AppendLine(
                "        protected override void RegisterChildBoards(UsefulToolkit.BlackBoard.BlackBoard.IBlackBoard blackBoard)");
            builder.AppendLine("        {");

            if (stateBoardTypes.Count == 0 && eventBoardTypes.Count == 0)
            {
                builder.AppendLine("            // 登録対象のChildBoardが見つかりませんでした。");
            }

            foreach (var boardType in stateBoardTypes)
            {
                string typeName = TypeName(boardType);
                builder.AppendLine($"            blackBoard.TryRegisterStateBoard(new {typeName}());");
            }

            foreach (var boardType in eventBoardTypes)
            {
                string typeName = TypeName(boardType);
                builder.AppendLine($"            blackBoard.TryRegisterEventBoard(new {typeName}());");
            }

            builder.AppendLine("        }");
            builder.AppendLine();
        }

        private static void AppendInjectAll(StringBuilder builder, IReadOnlyList<InitializerField> fields)
        {
            builder.AppendLine("        protected override void InjectAll()");
            builder.AppendLine("        {");

            bool wroteAny = false;

            foreach (var field in fields)
            {
                var injectables = GetInjectableInterfaces(field.InitializerType);
                if (injectables.Count == 0) continue;

                wroteAny = true;

                if (field.IsList)
                {
                    builder.AppendLine($"            foreach (var target in {field.FieldName})");
                    builder.AppendLine("            {");
                    builder.AppendLine("                if (target == null) continue;");
                    AppendInjectCalls(builder, injectables, "target", field.InitializerType, "                ");
                    builder.AppendLine("            }");
                }
                else
                {
                    builder.AppendLine($"            if ({field.FieldName} != null)");
                    builder.AppendLine("            {");
                    AppendInjectCalls(builder, injectables, field.FieldName, field.InitializerType, "                ");
                    builder.AppendLine("            }");
                }

                builder.AppendLine();
            }

            if (!wroteAny)
            {
                builder.AppendLine("            // IInjectableを実装したInitializerはありません。");
            }

            builder.AppendLine("        }");
            builder.AppendLine();
        }

        /// <summary>
        /// 1つのInitializerに対するInject呼び出しを書き出す。
        /// 引数の個数が違うIInjectableを複数実装できるため、実装ごとに独立したブロックを作る。
        /// </summary>
        private static void AppendInjectCalls(
            StringBuilder builder,
            IReadOnlyList<Type> injectables,
            string targetExpression,
            Type initializerType,
            string indent)
        {
            foreach (var injectable in injectables)
            {
                var dependencies = injectable.GetGenericArguments();

                var conditions = dependencies
                    .Select((dependency, index) => $"TryGetContent<{TypeName(dependency)}>(out var dep{index})")
                    .ToArray();

                var arguments = dependencies
                    .Select((_, index) => $"dep{index}")
                    .ToArray();

                // 変数名の衝突を避けるため、実装ごとにスコープを切る
                builder.AppendLine($"{indent}{{");
                builder.AppendLine($"{indent}    if ({string.Join(" && ", conditions)})");
                builder.AppendLine($"{indent}    {{");
                builder.AppendLine(
                    $"{indent}        (({TypeName(injectable)}){targetExpression}).Inject({string.Join(", ", arguments)});");
                builder.AppendLine($"{indent}    }}");
                builder.AppendLine($"{indent}    else");
                builder.AppendLine($"{indent}    {{");
                builder.AppendLine(
                    $"{indent}        UsefulToolkit.BlackBoard.Logger.UsefulLogger.LogWarning(" +
                    $"\"依存の解決に失敗しました : {initializerType.Name} ({DependencyNames(dependencies)})\", this);");
                builder.AppendLine($"{indent}    }}");
                builder.AppendLine($"{indent}}}");
            }
        }

        private static void AppendInitializeAll(StringBuilder builder, IReadOnlyList<InitializerField> fields)
        {
            builder.AppendLine(
                "        protected override void InitializeAll(UsefulToolkit.BlackBoard.BlackBoard.IBlackBoard blackBoard)");
            builder.AppendLine("        {");

            if (fields.Count == 0)
            {
                builder.AppendLine("            // 初期化対象のInitializerはありません。");
            }

            foreach (var field in fields)
            {
                if (field.IsList)
                {
                    builder.AppendLine($"            foreach (var target in {field.FieldName})");
                    builder.AppendLine("            {");
                    builder.AppendLine("                if (target != null) target.Initialize(blackBoard);");
                    builder.AppendLine("            }");
                }
                else
                {
                    builder.AppendLine(
                        $"            if ({field.FieldName} != null) {field.FieldName}.Initialize(blackBoard);");
                }
            }

            builder.AppendLine("        }");
        }

        /// <summary>
        /// 指定した型が実装しているIInjectableを列挙する。
        /// GetInterfacesは基底クラス経由の実装もフラットに返すため、継承の深さを気にしなくてよい。
        /// </summary>
        internal static IReadOnlyList<Type> GetInjectableInterfaces(Type type)
        {
            return type.GetInterfaces()
                .Where(i => i.IsGenericType && InjectableDefinitions.Contains(i.GetGenericTypeDefinition()))
                .OrderBy(i => i.GetGenericArguments().Length)
                .ToArray();
        }

        /// <summary>
        /// C#ソースに埋め込める完全修飾名へ変換する。usingの管理と同名クラスの衝突を避けるため、
        /// 短縮は一切行わない。
        /// </summary>
        private static string TypeName(Type type)
        {
            if (type.IsGenericType)
            {
                string definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
                // "Namespace.IInjectable`2" の "`2" を落とす
                int backtick = definition.IndexOf('`');
                if (backtick >= 0) definition = definition.Substring(0, backtick);

                string arguments = string.Join(", ", type.GetGenericArguments().Select(TypeName));
                return $"{definition.Replace('+', '.')}<{arguments}>";
            }

            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        private static string DependencyNames(IReadOnlyList<Type> dependencies)
        {
            return string.Join(", ", dependencies.Select(d => d.Name));
        }
    }
}
