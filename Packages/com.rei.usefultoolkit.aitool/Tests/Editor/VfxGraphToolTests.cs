using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Editor.Ai.Tests
{
    /// <summary>
    /// export-vfx-graph / edit-vfx-graph / list-vfx-nodes を、uloop から呼ばれるのと同じ入口で確かめる。
    /// VFX Graph の内部 API はリフレクションで呼んでいるので、VFX Graph を更新したときに壊れていないかをここで検出する。
    /// </summary>
    /// <remarks>
    /// 毎回 VFX Graph パッケージのテンプレート（06_Firework）を一時フォルダへ複製して使う。
    /// 編集はグラフ画面を開くので、後片付けで未保存のまま閉じる。
    /// </remarks>
    public class VfxGraphToolTests
    {
        private const string TemplatePath = "Packages/com.unity.visualeffectgraph/Editor/Templates/06_Firework.vfx";
        private const string TempFolder = "Assets/UsefulToolkitVfxGraphToolTests";
        private const string AssetName = "ToolTestFirework";
        private const string AssetPath = TempFolder + "/" + AssetName + ".vfx";

        private string _operationsFile;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
            Assert.IsTrue(AssetDatabase.CopyAsset(TemplatePath, AssetPath), "テンプレートを複製できません");
            _operationsFile = FileUtil.GetUniqueTempPathInProject() + ".json";
        }

        [TearDown]
        public void TearDown()
        {
            // 未保存の変更があると閉じるときに保存確認が出て止まるので、未保存の印を外してから閉じる。setter は protected
            PropertyInfo unsavedChanges = typeof(EditorWindow).GetProperty(nameof(EditorWindow.hasUnsavedChanges));
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.titleContent.text == AssetName))
            {
                unsavedChanges?.SetValue(window, false);
                window.Close();
            }

            AssetDatabase.DeleteAsset(TempFolder);
            if (File.Exists(_operationsFile)) File.Delete(_operationsFile);
        }

        [Test]
        public void Export_ReturnsSystemsAndStableRevision()
        {
            var first = Export();
            var second = Export();

            Assert.IsTrue(first.Success, first.ErrorMessage);
            StringAssert.Contains("## Systems", first.Markdown);
            StringAssert.Contains("#### c1 Spawn", first.Markdown);
            StringAssert.Contains("evt <- c3.b3.evt", first.Markdown);
            StringAssert.IsMatch("^[0-9a-f]{12}$", first.Revision);
            Assert.AreEqual(first.Revision, second.Revision);
        }

        [Test]
        public void Export_WithNonVfxAsset_Fails()
        {
            var response = Run<ExportVfxGraphResponse>(new ExportVfxGraphTool(), new { assetPath = "Assets/NotExisting.vfx" });

            Assert.IsFalse(response.Success);
            Assert.IsNotEmpty(response.ErrorMessage);
        }

        [Test]
        public void Edit_WithStaleRevision_IsRejected()
        {
            EditVfxGraphResponse response = Edit("000000000000", new JArray(
                Op("setInput", new { target = "c2.b1", slot = "A", value = 2 })));

            Assert.IsFalse(response.Success);
            StringAssert.Contains("Revision", response.ErrorMessage);
        }

        [Test]
        public void Edit_AppliesChangesThroughGraphWindow()
        {
            string revision = Export().Revision;

            EditVfxGraphResponse response = Edit(revision, new JArray(
                Op("setInput", new { target = "c2.b1", slot = "A", value = 2 }),
                Op("setInput", new { target = "c2.b3", slot = "arcSphere.sphere.radius", value = 0.1 }),
                Op("setSetting", new { target = "c5", setting = "blendMode", value = "Additive" }),
                Op("addBlock", new { context = "c3", type = "Turbulence", at = 2, @as = "turb" }),
                Op("setInput", new { target = "$turb", slot = "Intensity", value = 3 }),
                Op("addOperator", new { type = "Random Float", @as = "rnd" }),
                Op("link", new { from = "$rnd", to = "c2.b1", toSlot = "B" }),
                Op("addProperty", new { type = "Float", name = "Rate", exposed = true, value = 12, @as = "rate" }),
                Op("link", new { from = "$rate", to = "c1.b1", toSlot = "Rate" }),
                Op("setBlockEnabled", new { target = "c3.b2", enabled = false })));

            Assert.IsTrue(response.Success, response.ErrorMessage);
            Assert.AreEqual(10, response.AppliedOperations);
            Assert.AreEqual("c3.b2", response.Aliases["turb"]);
            Assert.AreEqual("o1", response.Aliases["rnd"]);
            Assert.AreEqual("p1", response.Aliases["rate"]);
            StringAssert.Contains("A = 2 *", response.Markdown);
            StringAssert.Contains("B <- o1.r", response.Markdown);
            StringAssert.Contains("radius=0.1}", response.Markdown);
            StringAssert.Contains("blendMode=Additive", response.Markdown);
            StringAssert.Contains("c3.b2 Turbulence", response.Markdown);
            StringAssert.Contains("Intensity = 3 *", response.Markdown);
            StringAssert.Contains("p1 **Rate** : Single = 12 (exposed)", response.Markdown);
            StringAssert.Contains("Rate <- p1", response.Markdown);
            // c3.b2 は編集前のグラフの ID なので、追加した Turbulence ではなく元の Linear Drag を指す
            StringAssert.Contains("c3.b3 Linear Drag `Drag` [disabled]", response.Markdown);
            Assert.IsFalse(response.Saved);
            Assert.AreEqual(response.Markdown, Export().Markdown);
        }

        [Test]
        public void Edit_LinksFromOperatorWhoseTypeChanges()
        {
            // Multiply は入力の型に合わせて出力スロットを作り直すので、古いスロットに繋がないことを確かめる
            EditVfxGraphResponse response = Edit(Export().Revision, new JArray(
                Op("addOperator", new { type = "Get Direction", @as = "dir" }),
                Op("addOperator", new { type = "Random Float", @as = "speed" }),
                Op("addOperator", new { type = "Multiply", @as = "mul" }),
                Op("link", new { from = "$dir", to = "$mul", toSlot = "a" }),
                Op("link", new { from = "$speed", to = "$mul", toSlot = "b" }),
                Op("link", new { from = "$mul", to = "c3.b1", toSlot = "Force" })));

            Assert.IsTrue(response.Success, response.ErrorMessage);
            StringAssert.Contains($"Force <- {response.Aliases["mul"]}", response.Markdown);
            StringAssert.Contains("Multiply (Vector3)", response.Markdown);
        }

        [Test]
        public void Edit_SetsOperandType()
        {
            EditVfxGraphResponse response = Edit(Export().Revision, new JArray(
                Op("addOperator", new { type = "Sample Graphics Buffer", @as = "buffer" }),
                Op("setOperandType", new { target = "$buffer", type = "Vector4" }),
                Op("addOperator", new { type = "Multiply", @as = "mul" }),
                Op("setOperandType", new { target = "$mul", operand = "a", type = "Vector3" })));

            Assert.IsTrue(response.Success, response.ErrorMessage);
            StringAssert.Contains("- Type: Vector4", response.Markdown);
            StringAssert.Contains("- Operand types: a=Vector3, b=float", response.Markdown);
        }

        [Test]
        public void Edit_WithInvalidOperandType_ListsValidTypes()
        {
            EditVfxGraphResponse response = Edit(Export().Revision, new JArray(
                Op("addOperator", new { type = "Sample Graphics Buffer", @as = "buffer" }),
                Op("setOperandType", new { target = "$buffer", type = "NoSuchType" })));

            Assert.IsFalse(response.Success);
            StringAssert.Contains("Vector4", response.ErrorMessage);
        }

        [Test]
        public void Edit_RemovesAndUnlinks()
        {
            string revision = Export().Revision;

            EditVfxGraphResponse response = Edit(revision, new JArray(
                Op("remove", new { target = "c3.b2" }),
                Op("unlink", new { to = "c4", toSlot = "evt" }),
                Op("unlinkFlow", new { from = "c1", to = "c2" })));

            Assert.IsTrue(response.Success, response.ErrorMessage);
            // Linear Drag は 2 つの System にあり、c3 の分だけが消える
            Assert.AreEqual(1, response.Markdown.Split("Linear Drag").Length - 1);
            StringAssert.Contains("c3.b2 Trigger Event On Die", response.Markdown);
            StringAssert.DoesNotContain("evt <-", response.Markdown);
            StringAssert.DoesNotContain("- Flow: -> c2", response.Markdown);
        }

        [Test]
        public void Edit_LinkFlowRestoresContextLink()
        {
            string revision = Edit(Export().Revision, new JArray(Op("unlinkFlow", new { from = "c1", to = "c2" }))).Revision;

            EditVfxGraphResponse response = Edit(revision, new JArray(Op("linkFlow", new { from = "c1", to = "c2" })));

            Assert.IsTrue(response.Success, response.ErrorMessage);
            StringAssert.Contains("- Flow: -> c2", response.Markdown);
        }

        [Test]
        public void Edit_FailingOperation_RevertsAllChanges()
        {
            ExportVfxGraphResponse before = Export();
            string revision = before.Revision;

            EditVfxGraphResponse response = Edit(revision, new JArray(
                Op("setInput", new { target = "c2.b1", slot = "A", value = 2 }),
                Op("addBlock", new { context = "c3", type = "Turbulence", @as = "turb" }),
                Op("setInput", new { target = "$turb", slot = "NoSuchSlot", value = 1 })));

            Assert.IsFalse(response.Success);
            StringAssert.Contains("operations[2]", response.ErrorMessage);
            Assert.AreEqual(before.Markdown, Export().Markdown);
        }

        [Test]
        public void Edit_CanBeUndoneAsOneStep()
        {
            ExportVfxGraphResponse before = Export();
            EditVfxGraphResponse response = Edit(before.Revision, new JArray(
                Op("setInput", new { target = "c2.b1", slot = "A", value = 2 }),
                Op("addOperator", new { type = "Random Float" })));
            Assert.IsTrue(response.Success, response.ErrorMessage);

            Undo.PerformUndo();

            Assert.AreEqual(before.Markdown, Export().Markdown);
        }

        [Test]
        public void Edit_WithSave_WritesAsset()
        {
            EditVfxGraphResponse response = Edit(Export().Revision, new JArray(
                Op("addProperty", new { type = "Float", name = "SavedByToolTest", exposed = true })), save: true);

            Assert.IsTrue(response.Success, response.ErrorMessage);
            Assert.IsTrue(response.Saved);
            StringAssert.Contains("SavedByToolTest", File.ReadAllText(AssetPath));
        }

        [Test]
        public void ListNodes_FindsBlockByQuery()
        {
            var response = Run<ListVfxNodesResponse>(new ListVfxNodesTool(), new { kind = "Block", query = "turbulence" });

            Assert.IsTrue(response.Success, response.ErrorMessage);
            Assert.IsTrue(response.Nodes.Any(n => n.Name == "Turbulence"));
        }

        private ExportVfxGraphResponse Export()
        {
            return Run<ExportVfxGraphResponse>(new ExportVfxGraphTool(), new { assetPath = AssetPath });
        }

        private EditVfxGraphResponse Edit(string revision, JArray operations, bool save = false)
        {
            File.WriteAllText(_operationsFile, operations.ToString());
            return Run<EditVfxGraphResponse>(new EditVfxGraphTool(),
                new { assetPath = AssetPath, revision, operationsFile = _operationsFile, save });
        }

        private static JObject Op(string name, object arguments)
        {
            JObject op = JObject.FromObject(arguments);
            op.AddFirst(new JProperty("op", name));
            return op;
        }

        private static T Run<T>(IUnityCliLoopTool tool, object parameters) where T : UnityCliLoopToolResponse
        {
            return (T)tool.ExecuteAsync(JObject.FromObject(parameters), CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
