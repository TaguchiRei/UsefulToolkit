using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using static UsefulToolkit.Editor.Ai.VfxGraphReflection;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// 操作リスト（JSON）を VFX Graph に適用する。
    /// モデルを直接書き換えず、グラフ画面のコントローラー（VFXViewController）を通して、人がグラフ画面で操作したときと同じ処理で変更する。
    /// </summary>
    /// <remarks>
    /// 対象アセットをグラフ画面で開き（閉じていれば開く）、操作全体を 1 つの Undo グループにまとめる。
    /// 途中で失敗したらそのグループを丸ごと取り消すので、変更は全部適用されるか、何も適用されないかのどちらかになる。
    /// ノードの指定には export-vfx-graph の ID を使うので、適用の前に Revision を照合して ID が古くなっていないことを確かめる。
    /// </remarks>
    internal static class VfxGraphEditor
    {
        private const string EnabledSlotName = "_vfx_enabled";
        private const float NewNodeSpacing = 160f;
        private const float NewNodeMargin = 450f;

        /// <summary>適用の結果。</summary>
        public sealed class Result
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";

            /// <summary>適用後のグラフの Markdown。保存していない変更も含む。</summary>
            public string Markdown { get; set; } = "";

            /// <summary>適用後のグラフの Revision。</summary>
            public string Revision { get; set; } = "";

            public int AppliedOperations { get; set; }
            public bool Saved { get; set; }

            /// <summary>適用はできたが、確かめてほしいこと（コンパイルの失敗など）。</summary>
            public List<string> Warnings { get; set; } = new();

            /// <summary>操作の as で付けた別名と、追加されたノードの ID の対応。</summary>
            public Dictionary<string, string> Aliases { get; set; } = new();
        }

        /// <summary>
        /// 操作リストを適用する。
        /// </summary>
        /// <param name="assetPath">Assets/ または Packages/ から始まる VFX Graph アセットのパス。</param>
        /// <param name="revision">export-vfx-graph が返した Revision。</param>
        /// <param name="operations">操作の配列。</param>
        /// <param name="save">true のときはグラフ画面の保存処理で保存する。false のときは未保存のまま画面に残す。</param>
        public static Result Apply(string assetPath, string revision, JArray operations, bool save)
        {
            var result = new Result();
            if (!TryLoadGraph(assetPath, out object resource, out _, out string error))
            {
                result.ErrorMessage = error;
                return result;
            }

            object window = OpenWindow(assetPath, resource);
            object view = Get(window, "graphView");
            object controller = Get(view, "controller");
            object graph = Get(controller, "graph");
            if (graph == null)
            {
                result.ErrorMessage = $"グラフ画面で開けませんでした: {assetPath}";
                return result;
            }

            string currentRevision = new VfxGraphNodeIds(graph).Revision;
            if (!string.Equals(revision?.Trim(), currentRevision, StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = $"Revision が一致しません（指定: {revision}、現在: {currentRevision}）。" +
                                      "書き出しの後にノードが追加・削除・並べ替えされ、ID が変わっています。export-vfx-graph で書き出し直し、新しい ID で操作を作ってください。";
                return result;
            }

            var session = new Session(controller, graph);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"AI: Edit {System.IO.Path.GetFileNameWithoutExtension(assetPath)}");
            int undoGroup = Undo.GetCurrentGroup();

            for (int i = 0; i < operations.Count; i++)
            {
                try
                {
                    if (operations[i] is not JObject operation) throw new EditException("操作は JSON オブジェクトで指定してください。");
                    session.Apply(operation);
                }
                catch (Exception e)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    string opName = (operations[i] as JObject)?["op"]?.ToString() ?? "?";
                    string reason = e is EditException ? e.Message : $"{e.GetType().Name}: {e.Message}";
                    result.ErrorMessage = $"operations[{i}] ({opName}) で失敗しました: {reason} 変更はすべて取り消しました。";
                    return result;
                }
            }

            try
            {
                Call(controller, "LightApplyChanges");
                session.VerifyNoDanglingLinks();
            }
            catch (Exception e)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                string reason = e is EditException ? e.Message : $"{e.GetType().Name}: {e.Message}";
                result.ErrorMessage = $"適用後の検査で失敗しました: {reason} 変更はすべて取り消しました。";
                return result;
            }

            Undo.IncrementCurrentGroup();
            result.AppliedOperations = operations.Count;

            // グラフ画面は次の更新でコンパイルするが、それを待たずにここでコンパイルする。
            // 表示される設定（Update の ageParticles など）はコンパイル時の属性の集計で決まるので、
            // コンパイル前に書き出すと、返す Markdown が実際のグラフと食い違う
            if (Get(resource, "isSubgraph") is not true && Call(graph, "IsExpressionGraphDirty") is true)
            {
                object output = Call(graph, "RecompileIfNeeded", false, true);
                if (Get(output, "success") is not true)
                {
                    result.Warnings.Add("グラフのコンパイルに失敗しました。Unity のコンソール（uloop get-logs）でエラーを確認してください。");
                }
            }

            if (save)
            {
                Call(view, "OnSave");
                result.Saved = true;
            }

            result.Markdown = VfxGraphTextExporter.Export(graph, assetPath, out string newRevision);
            result.Revision = newRevision;
            var ids = new VfxGraphNodeIds(graph);
            foreach (KeyValuePair<string, Object> alias in session.Aliases)
            {
                result.Aliases[alias.Key] = ids.IdOf(alias.Value) ?? "(removed)";
            }

            result.Success = true;
            return result;
        }

        /// <summary>
        /// アセットを表示しているグラフ画面を返す。なければ、ダブルクリックと同じ AssetDatabase.OpenAsset で開く。
        /// </summary>
        private static object OpenWindow(string assetPath, object resource)
        {
            object window = FindWindow(resource);
            if (window != null) return window;

            AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(assetPath));
            return FindWindow(resource);
        }

        private static object FindWindow(object resource)
        {
            var windows = CallStatic(VfxUiType("VFXViewWindow"), "GetAllWindows") as IEnumerable;
            return windows?.Cast<object>().FirstOrDefault(w => ReferenceEquals(Get(w, "displayedResource"), resource));
        }

        /// <summary>利用者に向けたエラー。メッセージをそのまま返す。</summary>
        private sealed class EditException : Exception
        {
            public EditException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// 1 回の適用。ID と別名を解決し、操作ごとにコントローラーを呼ぶ。
        /// </summary>
        private sealed class Session
        {
            private readonly object _controller;
            private readonly VfxGraphNodeIds _ids;
            private readonly Type _contextType = VfxType("VFXContext");
            private readonly Type _blockType = VfxType("VFXBlock");
            private readonly Type _parameterType = VfxType("VFXParameter");
            private readonly Rect _graphBounds;
            private int _newOperatorCount;
            private int _newContextCount;
            private int _newParameterNodeCount;

            public Session(object controller, object graph)
            {
                _controller = controller;
                _ids = new VfxGraphNodeIds(graph);
                _graphBounds = ComputeBounds(_ids);
            }

            public Dictionary<string, Object> Aliases { get; } = new();

            public void Apply(JObject op)
            {
                // 直前の操作で増えたノードのコントローラーを作らせる
                Call(_controller, "LightApplyChanges");

                string name = Text(op, "op");
                switch (name)
                {
                    case "setInput":
                        SetPortValue(FindInputPort(Node(op, "target"), Text(op, "slot")), Required(op, "value"));
                        break;
                    case "setBlockEnabled":
                        SetBlockEnabled(op);
                        break;
                    case "setSetting":
                        SetSetting(Node(op, "target"), Text(op, "setting"), Required(op, "value"));
                        break;
                    case "setOperandType":
                        SetOperandType(Node(op, "target"), Text(op, "type"), op["operand"]);
                        break;
                    case "setProperty":
                        SetProperty(op);
                        break;
                    case "link":
                        Link(op);
                        break;
                    case "unlink":
                        Unlink(op);
                        break;
                    case "linkFlow":
                    case "unlinkFlow":
                        LinkFlow(op, name == "linkFlow");
                        break;
                    case "remove":
                        Remove(Node(op, "target"));
                        break;
                    case "setPosition":
                        SetPosition(Node(op, "target"), Required(op, "position"));
                        break;
                    case "addBlock":
                        AddBlock(op);
                        break;
                    case "addOperator":
                        AddNode(op, VfxNodeKind.Operator);
                        break;
                    case "addContext":
                        AddNode(op, VfxNodeKind.Context);
                        break;
                    case "addProperty":
                        AddProperty(op);
                        break;
                    default:
                        throw new EditException($"op \"{name}\" はありません。");
                }
            }

            private void SetBlockEnabled(JObject op)
            {
                Object block = Node(op, "target");
                if (!_blockType.IsInstanceOfType(block)) throw new EditException("setBlockEnabled の target は Block（c1.b1 など）を指定してください。");
                SetPortValue(FindInputPort(block, EnabledSlotName), Required(op, "enabled"));
            }

            private static void SetSetting(Object model, string settingName, JToken value)
            {
                var settings = GetVisibleSettings(model);
                (string name, Type type, object current) setting = settings.FirstOrDefault(s => s.name == settingName);
                if (setting.name == null)
                {
                    throw new EditException($"設定 \"{settingName}\" はありません。指定できる設定: {string.Join(", ", settings.Select(s => s.name))}");
                }

                Call(model, "SetSettingValue", settingName, ConvertValue(value, setting.type, setting.current));
            }

            /// <summary>
            /// 型を選べる Operator の型を変える。グラフ画面の型のドロップダウン（VFXMultiOperatorEdit）と同じ呼び方をする。
            /// </summary>
            private static void SetOperandType(Object model, string typeName, JToken operand)
            {
                List<(string operand, Type type)> current = GetOperandTypes(model);
                if (current.Count == 0) throw new EditException($"{model.GetType().Name} には型の選択がありません。");

                var validTypes = ((IEnumerable)Get(model, "validTypes")).Cast<Type>().ToList();
                Type type = validTypes.FirstOrDefault(t => string.Equals(FriendlyTypeName(t), typeName, StringComparison.OrdinalIgnoreCase))
                            ?? validTypes.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new EditException($"型 \"{typeName}\" は選べません。選べる型: {string.Join(", ", validTypes.Select(FriendlyTypeName))}");

                // 型が 1 つだけの Operator（Sample Graphics Buffer など）
                if (current.Count == 1 && current[0].operand.Length == 0)
                {
                    if (operand != null) throw new EditException("この Operator の型は 1 つだけなので operand は指定できません。");
                    Call(model, "SetOperandType", type);
                    return;
                }

                int index = OperandIndex(current, operand);
                Call(model, "SetOperandType", index, type);

                // 同じ型にそろえる必要がある入力にも伝える。グラフ画面のドロップダウンと同じ規則
                if (!VfxType("IVFXOperatorNumericUnifiedConstrained").IsInstanceOfType(model)) return;
                var canBeScalar = ((IEnumerable)Get(model, "slotIndicesThatCanBeScalar")).Cast<int>().ToList();
                if (canBeScalar.Contains(index)) return;
                var matchingScalar = (Type)CallStatic(VfxUiType("VFXUnifiedConstraintOperatorController"), "GetMatchingScalar", type);
                foreach (int other in ((IEnumerable)Get(model, "slotIndicesThatMustHaveSameType")).Cast<int>().ToList())
                {
                    if (other != index && (!canBeScalar.Contains(other) || matchingScalar != (Type)Call(model, "GetOperandType", other)))
                    {
                        Call(model, "SetOperandType", other, type);
                    }
                }
            }

            /// <summary>operand（入力の名前か 0 始まりの番号）を番号にする。入力が 1 つなら省略できる。</summary>
            private static int OperandIndex(List<(string operand, Type type)> operands, JToken operand)
            {
                if (operand == null || operand.Type == JTokenType.Null)
                {
                    if (operands.Count == 1) return 0;
                    throw new EditException($"入力ごとに型を持つ Operator です。operand で入力を指定してください: {string.Join(", ", operands.Select(o => o.operand))}");
                }

                if (operand.Type == JTokenType.Integer)
                {
                    int index = operand.Value<int>();
                    if (index >= 0 && index < operands.Count) return index;
                }
                else
                {
                    int index = operands.FindIndex(o => string.Equals(o.operand, operand.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) return index;
                }

                throw new EditException($"operand \"{operand}\" はありません。指定できる入力: {string.Join(", ", operands.Select(o => o.operand))}");
            }

            private void SetProperty(JObject op)
            {
                Object parameter = Node(op, "target");
                if (!_parameterType.IsInstanceOfType(parameter)) throw new EditException("setProperty の target は Property（p1 など）を指定してください。");

                object parameterController = Call(_controller, "GetParameterController", parameter);
                if (op["name"] != null) Set(parameterController, "exposedName", Text(op, "name"));
                if (op["exposed"] != null) Set(parameterController, "exposed", (bool)ConvertValue(op["exposed"], typeof(bool), null));
                if (op["value"] != null)
                {
                    var type = (Type)Get(parameter, "type");
                    Set(parameterController, "value", ConvertValue(op["value"], type, Get(parameterController, "value")));
                }
            }

            private void Link(JObject op)
            {
                object input = FindInputPort(Node(op, "to"), op["toSlot"]?.ToString());
                object output = FindOutputPort(Node(op, "from"), op["fromSlot"]?.ToString());
                if (Call(_controller, "CreateLink", input, output, false) is not true)
                {
                    throw new EditException($"{Get(output, "path")} から {Get(input, "path")} へは接続できません（型が合わないか、循環しています）。");
                }
            }

            private void Unlink(JObject op)
            {
                object input = FindInputPort(Node(op, "to"), op["toSlot"]?.ToString());
                var edges = (Get(input, "connections") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                if (edges.Count == 0) throw new EditException($"スロット \"{Get(input, "path")}\" は接続されていません。");
                foreach (object edge in edges)
                {
                    Call(_controller, "RemoveElement", edge, true);
                }
            }

            private void LinkFlow(JObject op, bool link)
            {
                Object from = Node(op, "from");
                Object to = Node(op, "to");
                if (!_contextType.IsInstanceOfType(from) || !_contextType.IsInstanceOfType(to))
                {
                    throw new EditException("Flow の from / to は Context（c1 など）を指定してください。");
                }

                int fromSlot = op["fromSlot"]?.Value<int>() ?? 0;
                int toSlot = op["toSlot"]?.Value<int>() ?? 0;
                if (link)
                {
                    if (CallStatic(_contextType, "CanLink", from, to, fromSlot, toSlot) is not true)
                    {
                        throw new EditException($"{_ids.IdOf(from) ?? from.name} から {_ids.IdOf(to) ?? to.name} へ Flow を接続できません。");
                    }

                    Call(from, "LinkTo", to, fromSlot, toSlot);
                }
                else
                {
                    Call(from, "UnlinkTo", to, fromSlot, toSlot);
                }
            }

            private void Remove(Object model)
            {
                object element = _parameterType.IsInstanceOfType(model)
                    ? Call(_controller, "GetParameterController", model)
                    : NodeController(model);
                Call(_controller, "RemoveElement", element, true);
            }

            private void SetPosition(Object model, JToken position)
            {
                if (_blockType.IsInstanceOfType(model)) throw new EditException("Block は Context の中に並ぶので位置を指定できません。at で並び順を指定してください。");
                object node = _parameterType.IsInstanceOfType(model) ? ParameterNodeController(model) : NodeController(model);
                Set(node, "position", (Vector2)ConvertValue(position, typeof(Vector2), null));
            }

            private void AddBlock(JObject op)
            {
                Object context = Node(op, "context");
                if (!_contextType.IsInstanceOfType(context)) throw new EditException("addBlock の context は Context（c1 など）を指定してください。");

                VfxNodeCatalog.Entry entry = Find(VfxNodeKind.Block, op);
                var block = (Object)Call(entry.Variant, "CreateInstance");
                if (Call(context, "Accept", block, -1) is not true)
                {
                    Object.DestroyImmediate(block);
                    throw new EditException($"Block \"{entry.Name}\" はこの Context（{context.GetType().Name}）に追加できません。");
                }

                // at は追加後の番号（c1.b2 の 2）。省略時は末尾
                int index = op["at"] != null ? op["at"].Value<int>() - 1 : -1;
                Call(NodeController(context), "AddBlock", index, block, true);
                ApplySettings(block, op);
                RegisterAlias(op, block);
            }

            private void AddNode(JObject op, VfxNodeKind kind)
            {
                VfxNodeCatalog.Entry entry = Find(kind, op);
                Vector2 position = op["position"] != null
                    ? (Vector2)ConvertValue(op["position"], typeof(Vector2), null)
                    : NextPosition(kind);
                object nodeController = Call(_controller, "AddNode", position, entry.Variant, null)
                                        ?? throw new EditException($"{kind} \"{entry.Name}\" を追加できませんでした。");
                var model = (Object)Get(nodeController, "model");
                ApplySettings(model, op);
                RegisterAlias(op, model);
            }

            private void AddProperty(JObject op)
            {
                VfxNodeCatalog.Entry entry = Find(VfxNodeKind.Property, op);
                var parameter = (Object)Call(_controller, "AddVFXParameter", Vector2.zero, entry.Variant, true);
                Call(_controller, "LightApplyChanges");
                object parameterController = Call(_controller, "GetParameterController", parameter);
                if (op["name"] != null) Set(parameterController, "exposedName", Text(op, "name"));
                if (op["exposed"] != null) Set(parameterController, "exposed", (bool)ConvertValue(op["exposed"], typeof(bool), null));
                if (op["value"] != null)
                {
                    var type = (Type)Get(parameter, "type");
                    Set(parameterController, "value", ConvertValue(op["value"], type, Get(parameterController, "value")));
                }

                RegisterAlias(op, parameter);
            }

            private static void ApplySettings(Object model, JObject op)
            {
                if (op["settings"] is not JObject settings) return;
                foreach (JProperty setting in settings.Properties())
                {
                    SetSetting(model, setting.Name, setting.Value);
                }
            }

            private void RegisterAlias(JObject op, Object model)
            {
                string alias = op["as"]?.ToString();
                if (string.IsNullOrEmpty(alias)) return;
                if (!Aliases.TryAdd(alias.TrimStart('$'), model)) throw new EditException($"別名 \"{alias}\" はすでに使われています。");
            }

            private static VfxNodeCatalog.Entry Find(VfxNodeKind kind, JObject op)
            {
                if (!VfxNodeCatalog.TryFind(kind, Text(op, "type"), op["category"]?.ToString(), out VfxNodeCatalog.Entry entry, out string error))
                {
                    throw new EditException(error);
                }

                return entry;
            }

            /// <summary>ID（c1、c1.b2、o3、p1）か、同じ操作リストで付けた別名（$name）からモデルを引く。</summary>
            private Object Node(JObject op, string key)
            {
                string id = Text(op, key);
                Object model;
                if (id.StartsWith("$"))
                {
                    if (!Aliases.TryGetValue(id.Substring(1), out model)) throw new EditException($"別名 \"{id}\" は定義されていません。");
                }
                else if (!_ids.TryGetModel(id, out model))
                {
                    throw new EditException($"ノード \"{id}\" が見つかりません。");
                }

                if (model == null) throw new EditException($"ノード \"{id}\" はすでに削除されています。");
                return model;
            }

            /// <summary>モデルに対応するグラフ画面のノードのコントローラー。Block は所属する Context のコントローラーから探す。</summary>
            private object NodeController(Object model)
            {
                if (_blockType.IsInstanceOfType(model))
                {
                    object contextController = NodeController((Object)Call(model, "GetParent"));
                    object blockController = FindBlockController(contextController, model);
                    if (blockController == null)
                    {
                        // 追加したばかりの Block は、Context のコントローラーが同期するまで一覧に出ない
                        Call(contextController, "ApplyChanges");
                        blockController = FindBlockController(contextController, model);
                    }

                    return blockController ?? throw new EditException("Block のコントローラーが見つかりません。");
                }

                return Call(_controller, "GetNodeController", model, 0)
                       ?? throw new EditException($"{model.GetType().Name} のコントローラーが見つかりません。");
            }

            private static object FindBlockController(object contextController, Object block)
            {
                return (Get(contextController, "blockControllers") as IEnumerable)?.Cast<object>()
                    .FirstOrDefault(b => ReferenceEquals(Get(b, "model"), block));
            }

            /// <summary>Property は、グラフ上に置いた表示ノードが接続の起点になる。表示ノードがなければ置く。</summary>
            private object ParameterNodeController(Object parameter)
            {
                object parameterController = Call(_controller, "GetParameterController", parameter);
                object node = (Get(parameterController, "nodes") as IEnumerable)?.Cast<object>().FirstOrDefault();
                if (node != null) return node;

                var position = new Vector2(_graphBounds.xMin - NewNodeMargin * 1.5f, _graphBounds.yMin + NewNodeSpacing * _newParameterNodeCount++);
                return Call(_controller, "AddVFXParameter", position, parameterController, null);
            }

            /// <summary>
            /// ノードのコントローラーのポート一覧をモデルのスロットに合わせ直す。
            /// 型が変わる Operator（Multiply など）は接続のたびにスロットを作り直すので、合わせ直さないと古いスロットに繋いでしまう。
            /// </summary>
            private static object SyncedPorts(object nodeController)
            {
                Call(nodeController, "ApplyChanges");
                return nodeController;
            }

            /// <summary>
            /// グラフから外れたスロット（持ち主のいないスロット）への接続が残っていないかを調べる。
            /// 残っていれば、どこかの操作が古いスロットに繋いでいるので失敗にする。
            /// </summary>
            public void VerifyNoDanglingLinks()
            {
                var ids = new VfxGraphNodeIds(Get(_controller, "graph"));
                foreach (Object model in ids.Contexts.Concat(ids.Operators).Concat(ids.Parameters)
                             .Concat(ids.Contexts.SelectMany(VfxGraphNodeIds.Children)))
                {
                    var slots = new List<object>();
                    CollectSlots(Get(model, "inputSlots") as IEnumerable, slots);
                    CollectSlots(Get(model, "outputSlots") as IEnumerable, slots);
                    if (Get(model, "activationSlot") is { } activation) slots.Add(activation);
                    foreach (object slot in slots)
                    {
                        foreach (object linked in Get(slot, "LinkedSlots") as IEnumerable ?? Array.Empty<object>())
                        {
                            if (Get(linked, "owner") is not Object owner || owner == null)
                            {
                                throw new EditException($"{ids.IdOf(model) ?? model.name} のスロット \"{Get(slot, "name")}\" が、グラフから外れたスロットに接続されています。");
                            }
                        }
                    }
                }
            }

            private static void CollectSlots(IEnumerable slots, List<object> results)
            {
                foreach (object slot in slots ?? Array.Empty<object>())
                {
                    results.Add(slot);
                    CollectSlots(Get(slot, "children") as IEnumerable, results);
                }
            }

            private object FindInputPort(Object model, string slot)
            {
                var ports = (Get(SyncedPorts(NodeController(model)), "inputPorts") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                object port = ports.FirstOrDefault(p => string.Equals(Get(p, "path") as string, slot, StringComparison.Ordinal))
                              ?? ports.FirstOrDefault(p => string.Equals(Get(p, "path") as string, slot, StringComparison.OrdinalIgnoreCase));
                if (port == null)
                {
                    IEnumerable<string> names = ports.Select(p => Get(p, "path") as string).Where(p => p != EnabledSlotName);
                    throw new EditException($"入力スロット \"{slot}\" がありません。指定できるスロット: {string.Join(", ", names)}");
                }

                return port;
            }

            private object FindOutputPort(Object model, string slot)
            {
                object node = _parameterType.IsInstanceOfType(model) ? ParameterNodeController(model) : NodeController(model);
                var ports = (Get(SyncedPorts(node), "outputPorts") as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
                if (string.IsNullOrEmpty(slot))
                {
                    // 主スロットが 1 つだけなら省略できる（出力が 1 つの Operator と Property）
                    List<object> masters = ports.Where(p => (Get(p, "path") as string ?? "").IndexOf('.') < 0).ToList();
                    if (masters.Count == 1) return masters[0];
                    throw new EditException($"出力スロットが複数あります。fromSlot で指定してください: {string.Join(", ", ports.Select(p => Get(p, "path")))}");
                }

                // Property の出力スロットは主スロットの名前を省いて指定する（書き出しの p1.x と同じ）
                return ports.FirstOrDefault(p => PathMatches(Get(p, "path") as string, slot, _parameterType.IsInstanceOfType(model)))
                       ?? throw new EditException($"出力スロット \"{slot}\" がありません。指定できるスロット: {string.Join(", ", ports.Select(p => Get(p, "path")))}");
            }

            private static bool PathMatches(string portPath, string slot, bool skipMaster)
            {
                if (portPath == null) return false;
                if (string.Equals(portPath, slot, StringComparison.OrdinalIgnoreCase)) return true;
                int dot = portPath.IndexOf('.');
                return skipMaster && dot >= 0 && string.Equals(portPath.Substring(dot + 1), slot, StringComparison.OrdinalIgnoreCase);
            }

            private static void SetPortValue(object port, JToken value)
            {
                if (Get(port, "editable") is false)
                {
                    throw new EditException($"スロット \"{Get(port, "path")}\" は値を変更できません（接続されているか、上位のスロットが接続されています）。");
                }

                var type = (Type)Get(port, "portType");
                Set(port, "value", ConvertValue(value, type, Get(port, "value")));
            }

            private Vector2 NextPosition(VfxNodeKind kind)
            {
                // Operator は既存ノードの左、Context は右に縦に並べる。位置は人が後で整える前提
                return kind == VfxNodeKind.Context
                    ? new Vector2(_graphBounds.xMax + NewNodeMargin, _graphBounds.yMin + NewNodeSpacing * 3 * _newContextCount++)
                    : new Vector2(_graphBounds.xMin - NewNodeMargin, _graphBounds.yMin + NewNodeSpacing * _newOperatorCount++);
            }

            private static Rect ComputeBounds(VfxGraphNodeIds ids)
            {
                List<Vector2> positions = ids.Contexts.Concat(ids.Operators)
                    .Select(m => Get(m, "position"))
                    .OfType<Vector2>()
                    .ToList();
                if (positions.Count == 0) return new Rect(0, 0, 0, 0);
                return Rect.MinMaxRect(positions.Min(p => p.x), positions.Min(p => p.y), positions.Max(p => p.x), positions.Max(p => p.y));
            }

            private static string Text(JObject op, string key)
            {
                string text = op[key]?.ToString();
                if (string.IsNullOrEmpty(text)) throw new EditException($"\"{key}\" を指定してください。");
                return text;
            }

            private static JToken Required(JObject op, string key)
            {
                return op[key] ?? throw new EditException($"\"{key}\" を指定してください。");
            }
        }

        /// <summary>
        /// JSON の値を指定の型にする。current を渡すと、オブジェクト形式で一部のフィールドだけ指定したときに残りを current から引き継ぐ。
        /// </summary>
        private static object ConvertValue(JToken token, Type type, object current)
        {
            if (type == null) throw new EditException("値の型が分かりません。");
            if (token == null || token.Type == JTokenType.Null)
            {
                if (type.IsValueType) throw new EditException($"{type.Name} に null は指定できません。");
                return null;
            }

            try
            {
                if (type == typeof(float)) return token.Value<float>();
                if (type == typeof(double) || type == typeof(int) || type == typeof(uint) || type == typeof(bool) || type == typeof(string))
                {
                    return token.ToObject(type);
                }

                if (type.IsEnum) return ToEnum(token, type);
                if (type == typeof(Vector2)) return ToVector2(Floats(token, "xy", 2));
                if (type == typeof(Vector3)) return ToVector3(Floats(token, "xyz", 3));
                if (type == typeof(Vector4)) return ToVector4(Floats(token, "xyzw", 4));
                if (type == typeof(Color)) return ToColor(token);
                if (type == typeof(Gradient)) return ToGradient(token);
                if (type == typeof(AnimationCurve)) return ToCurve(token);
                if (typeof(Object).IsAssignableFrom(type)) return ToAsset(token, type);
            }
            catch (EditException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new EditException($"値 {token.ToString(Newtonsoft.Json.Formatting.None)} を {type.Name} にできません: {e.Message}");
            }

            return ToStruct(token, type, current);
        }

        private static object ToEnum(JToken token, Type type)
        {
            if (token.Type == JTokenType.Integer) return Enum.ToObject(type, token.Value<int>());
            string name = token.ToString();
            if (Enum.GetNames(type).FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return Enum.Parse(type, match);
            }

            throw new EditException($"{type.Name} に \"{name}\" はありません。指定できる値: {string.Join(", ", Enum.GetNames(type))}");
        }

        /// <summary>[x, y, z] か {"x":..,"y":..} から floats を取る。オブジェクト形式で省略した成分は 0。</summary>
        private static float[] Floats(JToken token, string axes, int count)
        {
            var values = new float[count];
            if (token is JArray array)
            {
                if (array.Count != count) throw new EditException($"要素が {count} 個の配列で指定してください: {array.ToString(Newtonsoft.Json.Formatting.None)}");
                for (int i = 0; i < count; i++) values[i] = array[i].Value<float>();
            }
            else if (token is JObject obj)
            {
                for (int i = 0; i < count; i++) values[i] = obj[axes[i].ToString()]?.Value<float>() ?? 0f;
            }
            else
            {
                throw new EditException($"[{string.Join(", ", axes.ToCharArray())}] の配列で指定してください。");
            }

            return values;
        }

        private static Vector2 ToVector2(float[] v) => new(v[0], v[1]);
        private static Vector3 ToVector3(float[] v) => new(v[0], v[1], v[2]);
        private static Vector4 ToVector4(float[] v) => new(v[0], v[1], v[2], v[3]);

        /// <summary>"#RRGGBB(AA)"、[r, g, b(, a)]、{"r":..} のいずれか。</summary>
        private static Color ToColor(JToken token)
        {
            if (token.Type == JTokenType.String)
            {
                if (ColorUtility.TryParseHtmlString(token.ToString(), out Color color)) return color;
                throw new EditException($"色 \"{token}\" を読めません。#RRGGBB か #RRGGBBAA で指定してください。");
            }

            if (token is JArray array && array.Count == 3) return new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
            if (token is JObject obj && obj["a"] == null) return new Color(obj["r"]?.Value<float>() ?? 0, obj["g"]?.Value<float>() ?? 0, obj["b"]?.Value<float>() ?? 0);
            float[] v = Floats(token, "rgba", 4);
            return new Color(v[0], v[1], v[2], v[3]);
        }

        /// <summary>{"mode": "Blend", "colors": [{"time": 0, "color": "#FFFFFF"}], "alphas": [{"time": 0, "alpha": 1}]}</summary>
        private static Gradient ToGradient(JToken token)
        {
            if (token is not JObject obj) throw new EditException("Gradient は {\"colors\": [...], \"alphas\": [...]} の形で指定してください。");

            var gradient = new Gradient();
            if (obj["mode"] != null) gradient.mode = (GradientMode)ToEnum(obj["mode"], typeof(GradientMode));
            GradientColorKey[] colors = (obj["colors"] as JArray ?? new JArray())
                .Select(k => new GradientColorKey(ToColor(k["color"]), k["time"]?.Value<float>() ?? 0f))
                .ToArray();
            GradientAlphaKey[] alphas = (obj["alphas"] as JArray ?? new JArray())
                .Select(k => new GradientAlphaKey(k["alpha"]?.Value<float>() ?? 1f, k["time"]?.Value<float>() ?? 0f))
                .ToArray();
            if (colors.Length == 0) colors = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
            if (alphas.Length == 0) alphas = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            gradient.SetKeys(colors, alphas);
            return gradient;
        }

        /// <summary>[[time, value], ...] か {"keys": [...], "preWrapMode": .., "postWrapMode": ..}。キーは {"time", "value", "inTangent", "outTangent"} も可。</summary>
        private static AnimationCurve ToCurve(JToken token)
        {
            JArray keys = token as JArray ?? (token as JObject)?["keys"] as JArray
                          ?? throw new EditException("AnimationCurve は [[time, value], ...] か {\"keys\": [...]} の形で指定してください。");

            var curve = new AnimationCurve(keys.Select(k => k is JArray pair
                ? new Keyframe(pair[0].Value<float>(), pair[1].Value<float>())
                : new Keyframe(k["time"]?.Value<float>() ?? 0f, k["value"]?.Value<float>() ?? 0f,
                    k["inTangent"]?.Value<float>() ?? 0f, k["outTangent"]?.Value<float>() ?? 0f)).ToArray());
            if (token is JObject obj)
            {
                if (obj["preWrapMode"] != null) curve.preWrapMode = (WrapMode)ToEnum(obj["preWrapMode"], typeof(WrapMode));
                if (obj["postWrapMode"] != null) curve.postWrapMode = (WrapMode)ToEnum(obj["postWrapMode"], typeof(WrapMode));
            }

            return curve;
        }

        /// <summary>アセットのパス。サブアセットは書き出しと同じ "パス:名前"。空文字は None。</summary>
        private static Object ToAsset(JToken token, Type type)
        {
            string text = token.ToString();
            if (text.Length == 0) return null;

            int separator = text.IndexOf(':', text.LastIndexOf('/') + 1);
            string path = separator >= 0 ? text.Substring(0, separator) : text;
            Object asset = separator >= 0
                ? AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(a => type.IsInstanceOfType(a) && a.name == text.Substring(separator + 1))
                : AssetDatabase.LoadAssetAtPath(path, type);
            return asset != null ? asset : throw new EditException($"{type.Name} のアセット \"{text}\" が見つかりません。");
        }

        /// <summary>
        /// VFX の型（Position、AABox、Sphere、Transform など）をオブジェクト形式のフィールド指定で作る。
        /// フィールドが 1 つだけの型（Position の position など）は、オブジェクトで包まずにそのフィールドの値を直接書ける。
        /// </summary>
        private static object ToStruct(JToken token, Type type, object current)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            if (fields.Length == 0) throw new EditException($"{type.Name} の値は指定できません。");

            object result = current != null && type.IsInstanceOfType(current) ? current : Activator.CreateInstance(type);
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    FieldInfo field = fields.FirstOrDefault(f => string.Equals(f.Name, property.Name, StringComparison.OrdinalIgnoreCase))
                                      ?? throw new EditException($"{type.Name} にフィールド \"{property.Name}\" はありません。指定できるフィールド: {string.Join(", ", fields.Select(f => f.Name))}");
                    field.SetValue(result, ConvertValue(property.Value, field.FieldType, field.GetValue(result)));
                }
            }
            else if (fields.Length == 1)
            {
                fields[0].SetValue(result, ConvertValue(token, fields[0].FieldType, fields[0].GetValue(result)));
            }
            else
            {
                throw new EditException($"{type.Name} は {{{string.Join(", ", fields.Select(f => $"\"{f.Name}\": ..."))}}} の形で指定してください。");
            }

            return result;
        }
    }
}
