using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// VFX Graph アセットを、構造と値だけを残した Markdown にする。
    /// VFX Graph の編集用モデル（VFXGraph / VFXContext / VFXBlock / VFXSlot など）はすべて internal なので、
    /// 型名から引いてリフレクションで辿る。VFX Graph の内部実装への依存はこのクラスに閉じ込める。
    /// </summary>
    /// <remarks>
    /// 入力スロットは値がデフォルトのものも含めてすべて出し、デフォルトから変わった値に <c>*</c> を付ける。
    /// デフォルトは各ノードの inputProperties（InputProperties クラスのフィールド初期値）から取る。
    /// </remarks>
    internal static class VfxGraphTextExporter
    {
        private const string VfxEditorAssembly = "Unity.VisualEffectGraph.Editor";
        private const string VfxModuleAssembly = "UnityEditor.VFXModule";
        private const int MaxValueDepth = 3;
        private const int MaxListItems = 16;

        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // VFXSettingAttribute.VisibleFlags の InInspector / InGraph。どちらかで表示される設定を出す。
        private const int SettingVisibleInInspector = 1 << 0;
        private const int SettingVisibleInGraph = 1 << 1;

        /// <summary>
        /// 指定パスの VFX Graph を Markdown にする。
        /// </summary>
        /// <param name="assetPath">Assets/ または Packages/ から始まるアセットのパス。</param>
        /// <param name="markdown">成功時の Markdown。</param>
        /// <param name="error">失敗時の理由。</param>
        public static bool TryExport(string assetPath, out string markdown, out string error)
        {
            markdown = "";
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "AssetPath が空です。Assets/ または Packages/ から始まる VFX Graph アセットのパスを指定してください。";
                return false;
            }

            Type resourceType = Type.GetType($"UnityEditor.VFX.VisualEffectResource, {VfxModuleAssembly}");
            Type extensionsType = Type.GetType($"UnityEditor.VFX.VisualEffectResourceExtensions, {VfxEditorAssembly}");
            if (resourceType == null || extensionsType == null)
            {
                error = "VFX Graph パッケージ (com.unity.visualeffectgraph) が見つかりません。";
                return false;
            }

            object resource = resourceType.GetMethod("GetResourceAtPath", StaticFlags)?.Invoke(null, new object[] { assetPath });
            if (resource == null)
            {
                error = $"VFX Graph のアセットとして読み込めません: {assetPath}";
                return false;
            }

            try
            {
                object graph = extensionsType.GetMethod("GetOrCreateGraph", StaticFlags, null, new[] { resourceType }, null)
                    ?.Invoke(null, new[] { resource });
                if (graph == null)
                {
                    error = $"グラフを取得できません: {assetPath}";
                    return false;
                }

                markdown = new Writer(graph).Write(assetPath);
                error = "";
                return true;
            }
            catch (Exception e)
            {
                // VFX Graph の内部実装が変わってリフレクションが合わなくなったときはここに来る
                Exception inner = e is TargetInvocationException { InnerException: not null } ? e.InnerException : e;
                error = $"グラフの読み取りに失敗しました（VFX Graph のバージョンが想定と異なる可能性があります）: {inner}";
                return false;
            }
        }

        /// <summary>
        /// 1 つのグラフを書き出す。ノードに振る ID（c = Context、o = Operator、p = Parameter）を保持する。
        /// </summary>
        private sealed class Writer
        {
            private readonly object _graph;
            private readonly List<Object> _contexts = new();
            private readonly List<Object> _operators = new();
            private readonly List<Object> _parameters = new();
            private readonly Dictionary<Object, string> _ids = new();
            private readonly StringBuilder _sb = new();

            private readonly Type _contextType = VfxType("VFXContext");
            private readonly Type _parameterType = VfxType("VFXParameter");
            private readonly Type _slotContainerType = VfxType("IVFXSlotContainer");

            public Writer(object graph)
            {
                _graph = graph;
                foreach (Object model in Children(graph))
                {
                    if (_contextType.IsInstanceOfType(model)) _contexts.Add(model);
                    else if (_parameterType.IsInstanceOfType(model)) _parameters.Add(model);
                    else if (_slotContainerType.IsInstanceOfType(model)) _operators.Add(model);
                }

                AssignIds(_contexts, "c");
                AssignIds(_operators, "o");
                AssignIds(_parameters, "p");

                // Block も GPU Event の発火元などで接続元になるので、Context の ID の下に番号を振る
                foreach (Object context in _contexts)
                {
                    int index = 1;
                    foreach (Object block in Children(context))
                    {
                        _ids[block] = $"{_ids[context]}.b{index++}";
                    }
                }
            }

            public string Write(string assetPath)
            {
                _sb.AppendLine($"# VFX Graph: {System.IO.Path.GetFileNameWithoutExtension(assetPath)}");
                _sb.AppendLine();
                _sb.AppendLine($"- Path: `{assetPath}`");
                _sb.AppendLine("- Legend: `name = value` is an input slot, `*` marks a value changed from the default, `<-` is the link source, IDs are c = context, o = operator, p = property");
                _sb.AppendLine();

                WriteProperties();
                WriteSystems();
                WriteOperators();
                WriteNotes();
                return _sb.ToString();
            }

            private void AssignIds(List<Object> models, string prefix)
            {
                for (int i = 0; i < models.Count; i++)
                {
                    _ids[models[i]] = $"{prefix}{i + 1}";
                }
            }

            private void WriteProperties()
            {
                if (_parameters.Count == 0) return;

                _sb.AppendLine("## Properties");
                _sb.AppendLine();
                foreach (Object parameter in _parameters)
                {
                    var type = Get(parameter, "type") as Type;
                    var attributes = new List<string>();
                    if (Get(parameter, "exposed") is true) attributes.Add("exposed");
                    if (Get(parameter, "category") is string { Length: > 0 } category) attributes.Add($"category \"{category}\"");
                    switch (Get(parameter, "valueFilter")?.ToString())
                    {
                        case "Range":
                            attributes.Add($"range {FormatValue(Get(parameter, "min"))}..{FormatValue(Get(parameter, "max"))}");
                            break;
                        case "Enum":
                            attributes.Add($"enum {FormatValue(Get(parameter, "enumValues"))}");
                            break;
                    }

                    if (Get(parameter, "tooltip") is string { Length: > 0 } tooltip) attributes.Add($"tooltip \"{tooltip}\"");

                    string valueText;
                    if (Get(parameter, "isOutput") is true)
                    {
                        // Subgraph の出力。値を持たず、入力スロットに繋がったものを外へ出す
                        attributes.Insert(0, "output");
                        object inputSlot = (Get(parameter, "inputSlots") as IEnumerable)?.Cast<object>().FirstOrDefault();
                        valueText = inputSlot != null && HasLink(inputSlot) ? $"<- {LinkSource(inputSlot)}" : "(unlinked)";
                    }
                    else
                    {
                        valueText = $"= {FormatValue(Get(parameter, "value"))}";
                    }

                    string suffix = attributes.Count > 0 ? $" ({string.Join(", ", attributes)})" : "";
                    _sb.AppendLine($"- {_ids[parameter]} **{Get(parameter, "exposedName")}** : {type?.Name ?? "?"} {valueText}{suffix}");
                }

                _sb.AppendLine();
            }

            private void WriteSystems()
            {
                if (_contexts.Count == 0) return;

                _sb.AppendLine("## Systems");
                _sb.AppendLine();

                // 同じ VFXData を共有する Context が 1 つの System。Data を持たない Context は単独で扱う
                var systems = new List<(object key, List<Object> contexts)>();
                foreach (Object context in _contexts)
                {
                    object key = Call(context, "GetData") ?? context;
                    int index = systems.FindIndex(s => ReferenceEquals(s.key, key));
                    if (index < 0)
                    {
                        systems.Add((key, new List<Object>()));
                        index = systems.Count - 1;
                    }

                    systems[index].contexts.Add(context);
                }

                MethodInfo getSystemName = VfxType("VFXSystemNames").GetMethod("GetSystemName", StaticFlags);
                for (int i = 0; i < systems.Count; i++)
                {
                    string name = getSystemName?.Invoke(null, new object[] { systems[i].contexts[0] }) as string;
                    _sb.AppendLine(string.IsNullOrEmpty(name) ? $"### System {i + 1}" : $"### System {i + 1} \"{name}\"");
                    _sb.AppendLine();
                    foreach (Object context in systems[i].contexts)
                    {
                        WriteContext(context);
                    }
                }
            }

            private void WriteContext(Object context)
            {
                string label = Get(context, "label") as string;
                string labelText = string.IsNullOrEmpty(label) ? "" : $" \"{label}\"";
                _sb.AppendLine($"#### {_ids[context]} {ModelName(context)}{labelText} `{context.GetType().Name}`");

                var flowOut = new List<string>();
                if (Get(context, "outputFlowSlot") is IEnumerable outputFlowSlots)
                {
                    int slotIndex = 0;
                    foreach (object flowSlot in outputFlowSlots)
                    {
                        foreach (object link in Get(flowSlot, "link") as IEnumerable ?? Array.Empty<object>())
                        {
                            var target = Get(link, "context") as Object;
                            int targetSlot = Get(link, "slotIndex") is int index ? index : 0;
                            string from = slotIndex > 0 ? $"#{slotIndex} " : "";
                            string to = targetSlot > 0 ? $"{IdOf(target)}#{targetSlot}" : IdOf(target);
                            flowOut.Add($"{from}-> {to}");
                        }

                        slotIndex++;
                    }
                }

                if (flowOut.Count > 0) _sb.AppendLine($"- Flow: {string.Join(", ", flowOut)}");
                WriteSettingsAndInputs(context, "");

                var blocks = Children(context).ToList();
                if (blocks.Count > 0)
                {
                    _sb.AppendLine("- Blocks:");
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        Object block = blocks[i];
                        string disabled = Get(block, "enabled") is false ? " [disabled]" : "";
                        _sb.AppendLine($"  {i + 1}. {_ids[block]} {ModelName(block)} `{block.GetType().Name}`{disabled}");
                        if (Get(block, "activationSlot") is { } activationSlot && HasLink(activationSlot))
                        {
                            _sb.AppendLine($"     - enabled <- {LinkSource(activationSlot)}");
                        }

                        WriteSettingsAndInputs(block, "     ");
                    }
                }

                _sb.AppendLine();
            }

            private void WriteOperators()
            {
                if (_operators.Count == 0) return;

                _sb.AppendLine("## Operators");
                _sb.AppendLine();
                foreach (Object op in _operators)
                {
                    _sb.AppendLine($"#### {_ids[op]} {ModelName(op)} `{op.GetType().Name}`");
                    WriteSettingsAndInputs(op, "");
                    _sb.AppendLine();
                }
            }

            private void WriteNotes()
            {
                object ui = Get(_graph, "UIInfos");
                var stickyNotes = Get(ui, "stickyNoteInfos") as IEnumerable;
                var groups = Get(ui, "groupInfos") as IEnumerable;

                var lines = new List<string>();
                foreach (object note in stickyNotes ?? Array.Empty<object>())
                {
                    string contents = (Get(note, "contents") as string ?? "").Replace("\r", "").Replace("\n", " / ");
                    lines.Add($"- Sticky note \"{Get(note, "title")}\": {contents}");
                }

                foreach (object group in groups ?? Array.Empty<object>())
                {
                    var members = new List<string>();
                    foreach (object nodeId in Get(group, "contents") as IEnumerable ?? Array.Empty<object>())
                    {
                        if (Get(nodeId, "model") is Object model && model != null) members.Add(IdOf(model));
                    }

                    lines.Add($"- Group \"{Get(group, "title")}\": {string.Join(", ", members.Distinct())}");
                }

                if (lines.Count == 0) return;

                _sb.AppendLine("## Notes");
                _sb.AppendLine();
                foreach (string line in lines) _sb.AppendLine(line);
                _sb.AppendLine();
            }

            private void WriteSettingsAndInputs(Object model, string indent)
            {
                string settings = FormatSettings(model);
                if (settings.Length > 0) _sb.AppendLine($"{indent}- Settings: {settings}");

                Dictionary<string, object> defaults = DefaultInputValues(model);
                foreach (object slot in Get(model, "inputSlots") as IEnumerable ?? Array.Empty<object>())
                {
                    _sb.AppendLine($"{indent}- {FormatInputSlot(slot, defaults)}");
                }
            }

            private string FormatInputSlot(object slot, Dictionary<string, object> defaults)
            {
                string name = Get(slot, "name") as string;
                string text;
                if (HasLink(slot))
                {
                    text = $"{name} <- {LinkSource(slot)}";
                }
                else
                {
                    object value = Get(slot, "value");
                    string valueText = FormatValue(value);
                    bool modified = defaults.TryGetValue(name ?? "", out object defaultValue)
                                    && defaultValue != null
                                    && valueText != FormatValue(defaultValue);
                    text = $"{name} = {valueText}{SpaceSuffix(slot)}{(modified ? " *" : "")}";
                }

                // Position の x だけを繋ぐような、子スロット単位の接続
                var childLinks = new List<string>();
                CollectChildLinks(slot, "", childLinks);
                return childLinks.Count > 0 ? $"{text}; {string.Join("; ", childLinks)}" : text;
            }

            private void CollectChildLinks(object slot, string path, List<string> results)
            {
                foreach (object child in Children(slot))
                {
                    string childPath = $"{path}.{Get(child, "name")}";
                    if (HasLink(child)) results.Add($"{childPath} <- {LinkSource(child)}");
                    else CollectChildLinks(child, childPath, results);
                }
            }

            private string LinkSource(object inputSlot)
            {
                var sources = new List<string>();
                foreach (object outputSlot in Get(inputSlot, "LinkedSlots") as IEnumerable ?? Array.Empty<object>())
                {
                    var owner = Get(outputSlot, "owner") as Object;
                    List<string> path = SlotPath(outputSlot);
                    // Parameter の主スロットは値そのものなので、スロット名を付けない
                    if (owner != null && _parameterType.IsInstanceOfType(owner) && path.Count > 0) path.RemoveAt(0);
                    // 出力が 1 つだけの Operator は出力スロットの名前が空
                    path.RemoveAll(string.IsNullOrEmpty);
                    sources.Add(path.Count > 0 ? $"{IdOf(owner)}.{string.Join(".", path)}" : IdOf(owner));
                }

                return string.Join(", ", sources);
            }

            private string IdOf(Object model)
            {
                if (model == null) return "?";
                return _ids.TryGetValue(model, out string id) ? id : ModelName(model);
            }

            /// <summary>主スロットから指定スロットまでのスロット名。</summary>
            private static List<string> SlotPath(object slot)
            {
                var names = new List<string>();
                object current = slot;
                while (current != null)
                {
                    names.Insert(0, Get(current, "name") as string ?? "");
                    if (Call(current, "IsMasterSlot") is true) break;
                    current = Call(current, "GetParent");
                }

                return names;
            }

            private static bool HasLink(object slot)
            {
                return Call(slot, "HasLink", false) is true;
            }

            private static string SpaceSuffix(object slot)
            {
                if (Get(slot, "spaceable") is not true) return "";
                string space = Get(slot, "space")?.ToString();
                return string.IsNullOrEmpty(space) || space == "None" ? "" : $" [{space}]";
            }

            private static string FormatSettings(Object model)
            {
                MethodInfo getSettings = model.GetType().GetMethods(InstanceFlags)
                    .FirstOrDefault(m => m.Name == "GetSettings" && m.GetParameters().Length == 2);
                if (getSettings == null) return "";

                Type flagsType = getSettings.GetParameters()[1].ParameterType;
                var entries = new List<string>();
                var names = new HashSet<string>();
                foreach (int flag in new[] { SettingVisibleInInspector, SettingVisibleInGraph })
                {
                    var settings = getSettings.Invoke(model, new[] { false, Enum.ToObject(flagsType, flag) }) as IEnumerable;
                    foreach (object setting in settings ?? Array.Empty<object>())
                    {
                        string name = Get(setting, "name") as string;
                        if (name == null || !names.Add(name)) continue;
                        entries.Add($"{name}={FormatValue(Get(setting, "value"))}");
                    }
                }

                return string.Join(", ", entries);
            }

            private static Dictionary<string, object> DefaultInputValues(Object model)
            {
                var defaults = new Dictionary<string, object>();
                try
                {
                    foreach (object property in Get(model, "inputProperties") as IEnumerable ?? Array.Empty<object>())
                    {
                        if (Get(Get(property, "property"), "name") is string name)
                        {
                            defaults[name] = Get(property, "value");
                        }
                    }
                }
                catch (Exception)
                {
                    // デフォルトが取れないノードは * を付けないだけにする
                }

                return defaults;
            }

            private static string ModelName(Object model)
            {
                string name = Get(model, "name") as string;
                if (string.IsNullOrEmpty(name)) name = model.GetType().Name;
                // ノード名は「|」区切りの表示用書式で、先頭の「_」は強調表示の印。改行も含みうる
                IEnumerable<string> words = name.Split('|', '\n', '\r')
                    .Select(w => w.Trim().TrimStart('_'))
                    .Where(w => w.Length > 0);
                return string.Join(" ", words);
            }

            private static IEnumerable<Object> Children(object model)
            {
                return (Get(model, "children") as IEnumerable)?.OfType<Object>() ?? Enumerable.Empty<Object>();
            }
        }

        private static string FormatValue(object value, int depth = 0)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string s:
                    return $"\"{s}\"";
                case bool b:
                    return b ? "true" : "false";
                case float f:
                    return FormatFloat(f);
                case double d:
                    return FormatFloat((float)d);
                case Enum e:
                    return e.ToString();
                case IConvertible convertible when value.GetType().IsPrimitive:
                    return convertible.ToString(CultureInfo.InvariantCulture);
                case Vector2 v:
                    return $"({FormatFloat(v.x)}, {FormatFloat(v.y)})";
                case Vector3 v:
                    return $"({FormatFloat(v.x)}, {FormatFloat(v.y)}, {FormatFloat(v.z)})";
                case Vector4 v:
                    return $"({FormatFloat(v.x)}, {FormatFloat(v.y)}, {FormatFloat(v.z)}, {FormatFloat(v.w)})";
                case Color c:
                    return $"RGBA({FormatFloat(c.r)}, {FormatFloat(c.g)}, {FormatFloat(c.b)}, {FormatFloat(c.a)})";
                case Matrix4x4 m:
                    return $"[{FormatValue(m.GetRow(0))}, {FormatValue(m.GetRow(1))}, {FormatValue(m.GetRow(2))}, {FormatValue(m.GetRow(3))}]";
                case Gradient g:
                    return FormatGradient(g);
                case AnimationCurve curve:
                    return FormatCurve(curve);
                case Object unityObject:
                    return FormatObject(unityObject);
                case Type type:
                    return type.Name;
                case IEnumerable enumerable:
                    return FormatList(enumerable, depth);
            }

            return depth < MaxValueDepth ? FormatFields(value, depth) : value.ToString();
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string FormatGradient(Gradient gradient)
        {
            string colors = string.Join(", ", gradient.colorKeys.Select(k =>
                $"{FormatFloat(k.time)}:#{ColorUtility.ToHtmlStringRGB(k.color)}"));
            string alphas = string.Join(", ", gradient.alphaKeys.Select(k => $"{FormatFloat(k.time)}:{FormatFloat(k.alpha)}"));
            return $"Gradient({gradient.mode}, colors [{colors}], alphas [{alphas}])";
        }

        private static string FormatCurve(AnimationCurve curve)
        {
            string keys = string.Join(", ", curve.keys.Select(k => $"({FormatFloat(k.time)}, {FormatFloat(k.value)})"));
            return $"Curve([{keys}], pre={curve.preWrapMode}, post={curve.postWrapMode})";
        }

        private static string FormatObject(Object unityObject)
        {
            if (unityObject == null) return "None";
            string path = AssetDatabase.GetAssetPath(unityObject);
            if (string.IsNullOrEmpty(path)) return unityObject.name;
            // サブアセットはファイル内の名前も付けないと区別できない
            return AssetDatabase.IsMainAsset(unityObject) ? path : $"{path}:{unityObject.name}";
        }

        private static string FormatList(IEnumerable enumerable, int depth)
        {
            var items = new List<string>();
            int count = 0;
            foreach (object item in enumerable)
            {
                if (count++ >= MaxListItems)
                {
                    items.Add("...");
                    break;
                }

                items.Add(FormatValue(item, depth + 1));
            }

            return $"[{string.Join(", ", items)}]";
        }

        private static string FormatFields(object value, int depth)
        {
            FieldInfo[] fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            if (fields.Length == 0) return value.ToString();
            return "{" + string.Join(", ", fields.Select(f => $"{f.Name}={FormatValue(f.GetValue(value), depth + 1)}")) + "}";
        }

        private static Type VfxType(string name)
        {
            return Type.GetType($"UnityEditor.VFX.{name}, {VfxEditorAssembly}", true);
        }

        private static object Get(object target, string memberName)
        {
            if (target == null) return null;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(memberName, InstanceFlags | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target);
                FieldInfo field = type.GetField(memberName, InstanceFlags | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);
            }

            return null;
        }

        private static object Call(object target, string methodName, params object[] args)
        {
            if (target == null) return null;
            MethodInfo method = target.GetType().GetMethods(InstanceFlags)
                .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == args.Length);
            return method?.Invoke(target, args);
        }
    }
}
