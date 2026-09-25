using UnityEditor;
using UnityEngine;

namespace Sandbox.Toon
{
    /// <summary>
    /// Sandbox/Toon のマテリアルインスペクター。
    /// 通常のプロパティ表示に加え、_OutlineEnabled の値に合わせてアウトラインパス（SRPDefaultUnlit）を有効／無効にする。
    /// </summary>
    public sealed class ToonShaderGUI : ShaderGUI
    {
        private const string OutlineEnabledProperty = "_OutlineEnabled";
        private const string OutlinePassLightMode = "SRPDefaultUnlit";

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);

            foreach (var target in materialEditor.targets)
            {
                if (target is Material material)
                {
                    ApplyOutlinePassState(material);
                }
            }
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            ApplyOutlinePassState(material);
        }

        /// <summary> _OutlineEnabled の値をアウトラインパスの有効状態へ反映する </summary>
        public static void ApplyOutlinePassState(Material material)
        {
            if (!material.HasProperty(OutlineEnabledProperty))
            {
                return;
            }

            bool enabled = material.GetFloat(OutlineEnabledProperty) > 0.5f;
            if (material.GetShaderPassEnabled(OutlinePassLightMode) != enabled)
            {
                material.SetShaderPassEnabled(OutlinePassLightMode, enabled);
                EditorUtility.SetDirty(material);
            }
        }
    }
}
