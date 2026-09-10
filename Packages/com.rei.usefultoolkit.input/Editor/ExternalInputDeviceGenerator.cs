using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UsefulToolkit.Editor.ProjectSettings;
using UsefulToolkit.Editor.Utility;
using UsefulToolkit.External.Input;

namespace UsefulToolkit.Editor.Input
{
    /// <summary>
    /// <see cref="ExternalInputDefinition"/>から、外部入力を受ける仮想InputDeviceを生成する。
    ///
    /// 生成するのは state 構造体・InputDevice派生・登録コード・スロットのenum・
    /// IExternalInputDeviceBridgeの実装の5つ。出力先フォルダと名前空間は
    /// UsefulToolkit/Settings のコード生成設定に従う。
    ///
    /// 生成後、InputActionAssetで <c>&lt;UsefulInput&gt;/スロット名</c> にバインドすると、
    /// 外部入力が通常のActionとして扱えるようになる。バインドしたActionは
    /// <see cref="InputActionEnumGenerator"/>が拾うため、ActionMaps/XxxActionsのenumにも載る。
    /// </summary>
    public static class ExternalInputDeviceGenerator
    {
        private const string FolderName = "Input";
        private const string DeviceClassName = "UsefulInputDevice";
        private const string StateStructName = "UsefulInputState";
        private const string RegistrationClassName = "UsefulInputDeviceRegistration";
        private const string BridgeClassName = "UsefulInputDeviceBridge";
        private const string SlotEnumName = "ExternalInputs";

        /// <summary> 生成物が名乗るデバイスのレイアウト名。InputActionAssetのパスに現れる </summary>
        private const string LayoutName = "UsefulInput";

        [MenuItem("UsefulToolkit/Input/Generate External Input Device")]
        public static void GenerateFromMenu()
        {
            var definition = FindDefinition();

            if (definition == null)
            {
                Debug.LogError(
                    "[UsefulToolkit.Input] ExternalInputDefinition が見つかりません。\n" +
                    "Create > UsefulToolkit > Input > External Input Definition で作成してください。");
                return;
            }

            Generate(definition);
        }

        /// <summary>
        /// 宣言から仮想デバイス一式を生成する。
        /// 名前の検証を全て通してから書き出すため、弾かれた場合は何も書き換わらない。
        /// </summary>
        /// <param name="definition">生成元の宣言</param>
        /// <returns>生成できた場合はtrue</returns>
        public static bool Generate(ExternalInputDefinition definition)
        {
            if (definition == null) return false;

            if (!TryCollectSlots(definition, out var slots)) return false;

            var ns = UsefulToolkitSettingsScriptable.instance.CodeGenerationSectionSettings.Namespace;

            FileGenerator.AutoGenerateFile($"{StateStructName}.cs", BuildStateSource(slots, ns),
                GenerateType.Runtime, FolderName);
            FileGenerator.AutoGenerateFile($"{DeviceClassName}.cs", BuildDeviceSource(slots, ns),
                GenerateType.Runtime, FolderName);
            FileGenerator.AutoGenerateFile($"{RegistrationClassName}.cs", BuildRegistrationSource(ns),
                GenerateType.Runtime, FolderName);
            FileGenerator.AutoGenerateFile($"{SlotEnumName}.cs",
                EnumGenerator.BuildSource(SlotEnumName, slots.Select(slot => slot.Name).ToArray(), ns),
                GenerateType.Runtime, FolderName);
            FileGenerator.AutoGenerateFile($"{BridgeClassName}.cs", BuildBridgeSource(slots, ns),
                GenerateType.Runtime, FolderName);

            WarnIfInputSystemNotReferenced();

            Debug.Log(
                $"[UsefulToolkit.Input] 外部入力デバイスを生成しました({slots.Count}スロット): " +
                $"{FileGenerator.GenerateRuntimeRootPath}/{GenerateType.Runtime}/{FolderName}\n" +
                $"InputActionAsset で <{LayoutName}>/スロット名 にバインドしてください。");

            return true;
        }

        /// <summary>
        /// 宣言からスロットを取り出し、名前を検証する。
        /// 空・識別子として使えない・重複のいずれかがあればエラーを出してfalseを返す。
        /// </summary>
        /// <param name="definition">生成元の宣言</param>
        /// <param name="slots">検証を通ったスロット</param>
        private static bool TryCollectSlots(ExternalInputDefinition definition, out List<ExternalInputSlot> slots)
        {
            slots = new List<ExternalInputSlot>();

            bool valid = true;
            var seen = new HashSet<string>();

            foreach (var slot in definition.Slots)
            {
                if (slot == null) continue;

                if (string.IsNullOrWhiteSpace(slot.Name))
                {
                    Debug.LogError("[UsefulToolkit.Input] 名前が空のスロットがあります。", definition);
                    valid = false;
                    continue;
                }

                if (!Regex.IsMatch(slot.Name, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                {
                    Debug.LogError(
                        $"[UsefulToolkit.Input] スロット名 '{slot.Name}' は識別子として使えません。" +
                        "英字またはアンダースコアで始まり、英数字とアンダースコアだけで構成してください。", definition);
                    valid = false;
                    continue;
                }

                if (!seen.Add(slot.Name))
                {
                    Debug.LogError(
                        $"[UsefulToolkit.Input] スロット名 '{slot.Name}' が重複しています。", definition);
                    valid = false;
                    continue;
                }

                slots.Add(slot);
            }

            if (!valid) return false;

            if (slots.Count != 0) return true;

            Debug.LogError("[UsefulToolkit.Input] スロットが1つも宣言されていない為、生成できません。", definition);
            return false;
        }

        /// <summary>
        /// state構造体のソースを組み立てる。
        ///
        /// Buttonもfloat 1つとして持たせる。uintへビット詰めすると
        /// QueueDeltaStateEventがビット単位のコントロールを扱えず、
        /// スロット単位の書き込みができなくなる為。
        /// </summary>
        /// <param name="slots">生成対象のスロット</param>
        /// <param name="ns">生成先の名前空間</param>
        private static string BuildStateSource(List<ExternalInputSlot> slots, string ns)
        {
            var builder = new StringBuilder();

            AppendHeader(builder);
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using UnityEngine.InputSystem.Layouts;");
            builder.AppendLine("using UnityEngine.InputSystem.LowLevel;");
            builder.AppendLine("using UnityEngine.InputSystem.Utilities;");
            builder.AppendLine();
            builder.AppendLine($"namespace {ns}");
            builder.AppendLine("{");
            builder.AppendLine($"    /// <summary>{DeviceClassName} が持つ状態。スロット1つにつきフィールド1つ。</summary>");
            builder.AppendLine($"    public struct {StateStructName} : IInputStateTypeInfo");
            builder.AppendLine("    {");
            builder.AppendLine("        public FourCC format => new FourCC('U', 'T', 'I', 'N');");

            foreach (var slot in slots)
            {
                builder.AppendLine();
                builder.AppendLine($"        [InputControl(name = \"{slot.Name}\", " +
                                   $"layout = \"{ControlLayoutOf(slot.ValueType)}\"" +
                                   $"{ControlFormatOf(slot.ValueType)})]");
                builder.AppendLine($"        public {FieldTypeOf(slot.ValueType)} {slot.Name};");
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        /// <summary>
        /// InputDevice派生のソースを組み立てる。
        /// </summary>
        /// <param name="slots">生成対象のスロット</param>
        /// <param name="ns">生成先の名前空間</param>
        private static string BuildDeviceSource(List<ExternalInputSlot> slots, string ns)
        {
            var builder = new StringBuilder();

            AppendHeader(builder);
            builder.AppendLine("using UnityEngine.InputSystem;");
            builder.AppendLine("using UnityEngine.InputSystem.Controls;");
            builder.AppendLine("using UnityEngine.InputSystem.Layouts;");
            builder.AppendLine();
            builder.AppendLine($"namespace {ns}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine("    /// 外部入力を受ける仮想デバイス。");
            builder.AppendLine($"    /// InputActionAsset からは &lt;{LayoutName}&gt;/スロット名 でバインドする。");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    [InputControlLayout(stateType = typeof({StateStructName}), " +
                               $"displayName = \"Useful Input\")]");
            builder.AppendLine($"    public sealed class {DeviceClassName} : InputDevice");
            builder.AppendLine("    {");

            foreach (var slot in slots)
            {
                builder.AppendLine($"        public {ControlTypeOf(slot.ValueType)} {slot.Name} " +
                                   "{ get; private set; }");
            }

            builder.AppendLine();
            builder.AppendLine("        protected override void FinishSetup()");
            builder.AppendLine("        {");
            builder.AppendLine("            base.FinishSetup();");
            builder.AppendLine();

            foreach (var slot in slots)
            {
                builder.AppendLine($"            {slot.Name} = " +
                                   $"GetChildControl<{ControlTypeOf(slot.ValueType)}>(\"{slot.Name}\");");
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        /// <summary>
        /// レイアウト登録とデバイス生成を行うコードを組み立てる。
        ///
        /// Editorでも登録しないとInputActionAssetのバインディング候補に現れない為、
        /// InitializeOnLoadとRuntimeInitializeOnLoadMethodの両方から通す。
        /// </summary>
        /// <param name="ns">生成先の名前空間</param>
        private static string BuildRegistrationSource(string ns)
        {
            var builder = new StringBuilder();

            AppendHeader(builder);
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using UnityEngine.InputSystem;");
            builder.AppendLine();
            builder.AppendLine($"namespace {ns}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// {DeviceClassName} のレイアウト登録とデバイス生成。");
            builder.AppendLine("    /// 利用者が呼ぶ必要はない。");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine("#if UNITY_EDITOR");
            builder.AppendLine("    [UnityEditor.InitializeOnLoad]");
            builder.AppendLine("#endif");
            builder.AppendLine($"    public static class {RegistrationClassName}");
            builder.AppendLine("    {");
            builder.AppendLine("#if UNITY_EDITOR");
            builder.AppendLine($"        static {RegistrationClassName}()");
            builder.AppendLine("        {");
            builder.AppendLine("            Register();");
            builder.AppendLine("        }");
            builder.AppendLine("#endif");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine("        /// レイアウトを登録し、デバイスが未生成なら生成する。");
            builder.AppendLine("        /// InputActionAssetがバインディングを解決するより前に通す必要がある。");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]");
            builder.AppendLine("        private static void Register()");
            builder.AppendLine("        {");
            builder.AppendLine($"            InputSystem.RegisterLayout<{DeviceClassName}>(\"{LayoutName}\");");
            builder.AppendLine();
            builder.AppendLine($"            if (InputSystem.GetDevice<{DeviceClassName}>() != null) return;");
            builder.AppendLine();
            builder.AppendLine($"            InputSystem.AddDevice<{DeviceClassName}>();");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        /// <summary>
        /// IExternalInputDeviceBridge実装のソースを組み立てる。
        ///
        /// 型の検査はUnsafeUtility.Asで行い、ボックス化を避ける。
        /// 毎フレーム呼ばれうる経路の為。
        /// </summary>
        /// <param name="slots">生成対象のスロット</param>
        /// <param name="ns">生成先の名前空間</param>
        private static string BuildBridgeSource(List<ExternalInputSlot> slots, string ns)
        {
            var builder = new StringBuilder();

            AppendHeader(builder);
            builder.AppendLine("using System;");
            builder.AppendLine("using Unity.Collections.LowLevel.Unsafe;");
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine("using UnityEngine.InputSystem;");
            builder.AppendLine("using UsefulToolkit.BlackBoard.Input;");
            builder.AppendLine();
            builder.AppendLine($"namespace {ns}");
            builder.AppendLine("{");
            builder.AppendLine("    /// <summary>");
            builder.AppendLine($"    /// 外部入力を {DeviceClassName} へ書き込む橋渡し。");
            builder.AppendLine("    /// 生成された InputInitializer が生成して InputManager へ渡す。");
            builder.AppendLine("    /// </summary>");
            builder.AppendLine($"    public sealed class {BridgeClassName} : IExternalInputDeviceBridge");
            builder.AppendLine("    {");
            builder.AppendLine($"        public bool TryWrite<TValue>(Enum slot, TValue value) " +
                               "where TValue : unmanaged");
            builder.AppendLine("        {");
            builder.AppendLine($"            if (slot is not {SlotEnumName} target)");
            builder.AppendLine("            {");
            builder.AppendLine($"                Debug.LogError($\"[UsefulToolkit.Input] " +
                               $"[{{slot}}] は {SlotEnumName} ではない為、書き込めません。\");");
            builder.AppendLine("                return false;");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine($"            var device = InputSystem.GetDevice<{DeviceClassName}>();");
            builder.AppendLine();
            builder.AppendLine("            if (device == null)");
            builder.AppendLine("            {");
            builder.AppendLine($"                Debug.LogError(\"[UsefulToolkit.Input] " +
                               $"{DeviceClassName} が生成されていない為、書き込めません。\");");
            builder.AppendLine("                return false;");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            switch (target)");
            builder.AppendLine("            {");

            foreach (var slot in slots)
            {
                string valueType = FieldTypeOf(slot.ValueType);

                builder.AppendLine($"                case {SlotEnumName}.{slot.Name}:");
                builder.AppendLine($"                    if (typeof(TValue) != typeof({valueType}))");
                builder.AppendLine($"                        return TypeMismatch(target, typeof({valueType}), " +
                                   "typeof(TValue));");
                builder.AppendLine($"                    InputSystem.QueueDeltaStateEvent(device.{slot.Name}, " +
                                   $"UnsafeUtility.As<TValue, {valueType}>(ref value));");
                builder.AppendLine("                    return true;");
                builder.AppendLine();
            }

            builder.AppendLine("                default:");
            builder.AppendLine($"                    Debug.LogError($\"[UsefulToolkit.Input] " +
                               "スロット [{target}] は生成されていません。\");");
            builder.AppendLine("                    return false;");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>");
            builder.AppendLine("        /// 宣言された型と書き込もうとした型が食い違う旨をエラーログに出す。");
            builder.AppendLine("        /// </summary>");
            builder.AppendLine($"        private static bool TypeMismatch({SlotEnumName} slot, " +
                               "Type expected, Type actual)");
            builder.AppendLine("        {");
            builder.AppendLine("            Debug.LogError($\"[UsefulToolkit.Input] スロット [{slot}] は " +
                               "{expected.Name} 型として宣言されている為、{actual.Name} 型では書き込めません。\");");
            builder.AppendLine("            return false;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");

            return builder.ToString();
        }

        private static void AppendHeader(StringBuilder builder)
        {
            builder.AppendLine("// このファイルはUsefulToolkit.Inputによって自動生成されています。直接編集しないでください。");
            builder.AppendLine();
        }

        /// <summary> スロットの値型に対応する、state構造体のフィールド型 </summary>
        private static string FieldTypeOf(ExternalInputValueType valueType)
        {
            return valueType switch
            {
                ExternalInputValueType.Vector2 => "Vector2",
                ExternalInputValueType.Vector3 => "Vector3",
                ExternalInputValueType.Quaternion => "Quaternion",
                ExternalInputValueType.Integer => "int",
                _ => "float",
            };
        }

        /// <summary> スロットの値型に対応する、InputSystemのコントロールレイアウト名 </summary>
        private static string ControlLayoutOf(ExternalInputValueType valueType)
        {
            return valueType switch
            {
                ExternalInputValueType.Vector2 => "Vector2",
                ExternalInputValueType.Vector3 => "Vector3",
                ExternalInputValueType.Quaternion => "Quaternion",
                ExternalInputValueType.Button => "Button",
                ExternalInputValueType.Integer => "Integer",
                _ => "Axis",
            };
        }

        /// <summary>
        /// Buttonだけは既定のフォーマットがビットの為、floatとして扱うことを明示する。
        /// </summary>
        private static string ControlFormatOf(ExternalInputValueType valueType)
        {
            return valueType == ExternalInputValueType.Button ? ", format = \"FLT\"" : string.Empty;
        }

        /// <summary> スロットの値型に対応する、InputSystemのコントロール型 </summary>
        private static string ControlTypeOf(ExternalInputValueType valueType)
        {
            return valueType switch
            {
                ExternalInputValueType.Vector2 => "Vector2Control",
                ExternalInputValueType.Vector3 => "Vector3Control",
                ExternalInputValueType.Quaternion => "QuaternionControl",
                ExternalInputValueType.Button => "ButtonControl",
                ExternalInputValueType.Integer => "IntegerControl",
                _ => "AxisControl",
            };
        }

        /// <summary>
        /// プロジェクト内のExternalInputDefinitionを1つ探す。
        /// 複数ある場合は最初に見つかったものを使い、警告を出す。
        /// </summary>
        private static ExternalInputDefinition FindDefinition()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(ExternalInputDefinition)}");

            if (guids.Length == 0) return null;

            if (guids.Length > 1)
            {
                Debug.LogWarning(
                    $"[UsefulToolkit.Input] ExternalInputDefinition が {guids.Length} 個あります。" +
                    "生成に使えるのは1つだけの為、最初に見つかったものを使います。");
            }

            return AssetDatabase.LoadAssetAtPath<ExternalInputDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>
        /// 生成先フォルダのasmdefがInputSystemを参照していない場合に警告する。
        /// 生成物はInputSystemの型を使う為、参照が無いとコンパイルが通らない。
        /// asmdefの自動編集は行わない。
        /// </summary>
        private static void WarnIfInputSystemNotReferenced()
        {
            string root = FileGenerator.GenerateRuntimeRootPath;

            if (string.IsNullOrEmpty(root)) return;

            string targetDirectory = NormalizeDirectory(
                Path.Combine(root, GenerateType.Runtime.ToString(), FolderName));

            string closestAsmdefPath = null;
            int closestLength = -1;

            // 生成先を含むasmdefのうち、最も近い(パスが最も長い)ものがそのコードを受け持つ
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string asmdefDirectory = NormalizeDirectory(Path.GetDirectoryName(assetPath));

                if (string.IsNullOrEmpty(asmdefDirectory)) continue;
                if (!targetDirectory.StartsWith(asmdefDirectory, System.StringComparison.Ordinal)) continue;
                if (asmdefDirectory.Length <= closestLength) continue;

                closestLength = asmdefDirectory.Length;
                closestAsmdefPath = assetPath;
            }

            if (closestAsmdefPath == null)
            {
                Debug.LogWarning(
                    "[UsefulToolkit.Input] 生成先を含むアセンブリ定義が見つかりません。" +
                    "生成したコードは Assembly-CSharp に入る為、InputSystem が参照できているか確認してください。");
                return;
            }

            if (File.ReadAllText(closestAsmdefPath).Contains("Unity.InputSystem")) return;

            Debug.LogWarning(
                $"[UsefulToolkit.Input] 生成先のアセンブリ定義 '{closestAsmdefPath}' が Unity.InputSystem を" +
                "参照していない為、生成したコードはコンパイルできません。参照へ追加してください。");
        }

        /// <summary>
        /// パスを比較できる形へ揃える。区切りをスラッシュにし、末尾にも区切りを付けて
        /// 「Assets/Code」が「Assets/CodeGen」の親と判定されないようにする。
        /// </summary>
        /// <param name="path">揃えるパス</param>
        private static string NormalizeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            string normalized = Path.GetFullPath(path).Replace('\\', '/');

            return normalized.EndsWith("/") ? normalized : normalized + "/";
        }
    }
}
