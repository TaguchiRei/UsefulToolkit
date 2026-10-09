using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Object = UnityEngine.Object;
using static UsefulToolkit.Editor.Ai.VfxGraphReflection;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// グラフのノードに振る ID。書き出しと編集で同じ ID を使うため、振り方はここだけで決める。
    /// </summary>
    /// <remarks>
    /// Context は c1、Operator は o1、Property は p1、Block は所属する Context の ID の下に c1.b1 と、
    /// それぞれグラフ上の並び順で振る。並び順が変わると ID も変わるので、編集では Revision で照合してから使う。
    /// Revision は ID とノードの型の対応だけのハッシュで、値や設定は含めない。
    /// 表示される設定はグラフのコンパイル状態で変わるため、それを含めると同じグラフでも Revision が揺れる。
    /// </remarks>
    internal sealed class VfxGraphNodeIds
    {
        private readonly Dictionary<Object, string> _ids = new();
        private readonly Dictionary<string, Object> _models = new();

        public VfxGraphNodeIds(object graph)
        {
            Type contextType = VfxType("VFXContext");
            Type parameterType = VfxType("VFXParameter");
            Type slotContainerType = VfxType("IVFXSlotContainer");

            foreach (Object model in Children(graph))
            {
                if (contextType.IsInstanceOfType(model)) Contexts.Add(model);
                else if (parameterType.IsInstanceOfType(model)) Parameters.Add(model);
                else if (slotContainerType.IsInstanceOfType(model)) Operators.Add(model);
            }

            Assign(Contexts, "c");
            Assign(Operators, "o");
            Assign(Parameters, "p");

            // Block も GPU Event の発火元などで接続元になるので、Context の ID の下に番号を振る
            foreach (Object context in Contexts)
            {
                int index = 1;
                foreach (Object block in Children(context))
                {
                    Add(block, $"{_ids[context]}.b{index++}");
                }
            }
        }

        /// <summary>ID とノードの型の対応のハッシュ（16 進 12 文字）。</summary>
        public string Revision
        {
            get
            {
                var text = new StringBuilder();
                foreach (KeyValuePair<string, Object> pair in _models.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    text.Append(pair.Key).Append('=').Append(pair.Value.GetType().FullName).Append('\n');
                }

                using var sha1 = SHA1.Create();
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
                return BitConverter.ToString(hash, 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Context。グラフ上の並び順。</summary>
        public List<Object> Contexts { get; } = new();

        /// <summary>Operator。グラフ上の並び順。</summary>
        public List<Object> Operators { get; } = new();

        /// <summary>Property（VFXParameter）。グラフ上の並び順。</summary>
        public List<Object> Parameters { get; } = new();

        /// <summary>ID を持たないモデルには null。</summary>
        public string IdOf(Object model)
        {
            return model != null && _ids.TryGetValue(model, out string id) ? id : null;
        }

        public bool TryGetModel(string id, out Object model)
        {
            return _models.TryGetValue(id ?? "", out model);
        }

        /// <summary>モデルの子（Context なら Block、スロットなら子スロット）。</summary>
        public static IEnumerable<Object> Children(object model)
        {
            return (Get(model, "children") as IEnumerable)?.OfType<Object>() ?? Enumerable.Empty<Object>();
        }

        private void Assign(List<Object> models, string prefix)
        {
            for (int i = 0; i < models.Count; i++)
            {
                Add(models[i], $"{prefix}{i + 1}");
            }
        }

        private void Add(Object model, string id)
        {
            _ids[model] = id;
            _models[id] = model;
        }
    }
}
