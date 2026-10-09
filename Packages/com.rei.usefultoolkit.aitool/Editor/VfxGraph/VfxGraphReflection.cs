using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UsefulToolkit.Editor.Ai
{
    /// <summary>
    /// VFX Graph の internal な型・メンバーにリフレクションで触るための共通処理。
    /// VFX Graph の編集用モデルとグラフ画面のコントローラーは公開されていないので、型名とメンバー名で引く。
    /// </summary>
    internal static class VfxGraphReflection
    {
        private const string VfxEditorAssembly = "Unity.VisualEffectGraph.Editor";
        private const string VfxModuleAssembly = "UnityEditor.VFXModule";

        public const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // VFXSettingAttribute.VisibleFlags の InInspector / InGraph
        private const int SettingVisibleInInspector = 1 << 0;
        private const int SettingVisibleInGraph = 1 << 1;

        /// <summary>VFX Graph パッケージが入っているか。</summary>
        public static bool IsVfxGraphAvailable =>
            Type.GetType($"UnityEditor.VFX.VFXGraph, {VfxEditorAssembly}") != null;

        /// <summary>UnityEditor.VFX 名前空間の型。見つからなければ例外。</summary>
        public static Type VfxType(string name)
        {
            return Type.GetType($"UnityEditor.VFX.{name}, {VfxEditorAssembly}", true);
        }

        /// <summary>グラフ画面側（UnityEditor.VFX.UI 名前空間）の型。見つからなければ例外。</summary>
        public static Type VfxUiType(string name)
        {
            return Type.GetType($"UnityEditor.VFX.UI.{name}, {VfxEditorAssembly}", true);
        }

        /// <summary>
        /// アセットのパスから VisualEffectResource とその VFXGraph を取る。
        /// </summary>
        public static bool TryLoadGraph(string assetPath, out object resource, out object graph, out string error)
        {
            resource = null;
            graph = null;
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

            resource = resourceType.GetMethod("GetResourceAtPath", StaticFlags)?.Invoke(null, new object[] { assetPath });
            if (resource == null)
            {
                error = $"VFX Graph のアセットとして読み込めません: {assetPath}";
                return false;
            }

            graph = extensionsType.GetMethod("GetOrCreateGraph", StaticFlags, null, new[] { resourceType }, null)
                ?.Invoke(null, new[] { resource });
            if (graph == null)
            {
                error = $"グラフを取得できません: {assetPath}";
                return false;
            }

            error = "";
            return true;
        }

        /// <summary>
        /// グラフ画面かインスペクターに表示される設定（VFXSetting）。非表示の内部設定は含めない。
        /// </summary>
        public static List<(string name, Type type, object value)> GetVisibleSettings(object model)
        {
            var result = new List<(string, Type, object)>();
            MethodInfo getSettings = model.GetType().GetMethods(InstanceFlags)
                .FirstOrDefault(m => m.Name == "GetSettings" && m.GetParameters().Length == 2);
            if (getSettings == null) return result;

            Type flagsType = getSettings.GetParameters()[1].ParameterType;
            var names = new HashSet<string>();
            foreach (int flag in new[] { SettingVisibleInInspector, SettingVisibleInGraph })
            {
                var settings = getSettings.Invoke(model, new[] { false, Enum.ToObject(flagsType, flag) }) as IEnumerable;
                foreach (object setting in settings ?? Array.Empty<object>())
                {
                    string name = Get(setting, "name") as string;
                    if (name == null || !names.Add(name)) continue;
                    var field = Get(setting, "field") as FieldInfo;
                    result.Add((name, field?.FieldType, Get(setting, "value")));
                }
            }

            return result;
        }

        /// <summary>
        /// プロパティまたはフィールドの値。派生側で宣言されたものを優先する。見つからなければ null。
        /// </summary>
        public static object Get(object target, string memberName)
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

        /// <summary>
        /// 書き込めるプロパティに値を入れる。見つからなければ例外。
        /// </summary>
        public static void Set(object target, string propertyName, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(propertyName, InstanceFlags | BindingFlags.DeclaredOnly);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, value);
                    return;
                }
            }

            throw new MissingMemberException(target.GetType().Name, propertyName);
        }

        /// <summary>
        /// 引数の型が合うメソッドを呼ぶ。null の引数は参照型の引数に合うものとして扱う。見つからなければ例外。
        /// </summary>
        public static object Call(object target, string methodName, params object[] args)
        {
            MethodInfo method = FindMethod(target.GetType(), methodName, InstanceFlags, args)
                                ?? throw new MissingMethodException(target.GetType().Name, methodName);
            return Invoke(method, target, args);
        }

        /// <summary>
        /// 型の static メソッドを呼ぶ。見つからなければ例外。
        /// </summary>
        public static object CallStatic(Type type, string methodName, params object[] args)
        {
            MethodInfo method = FindMethod(type, methodName, StaticFlags, args)
                                ?? throw new MissingMethodException(type.Name, methodName);
            return Invoke(method, null, args);
        }

        private static MethodInfo FindMethod(Type type, string methodName, BindingFlags flags, object[] args)
        {
            return type.GetMethods(flags).FirstOrDefault(m => m.Name == methodName && Accepts(m.GetParameters(), args));
        }

        private static bool Accepts(ParameterInfo[] parameters, object[] args)
        {
            if (parameters.Length != args.Length) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                if (args[i] == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null) return false;
                }
                else if (!parameterType.IsInstanceOfType(args[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static object Invoke(MethodInfo method, object target, object[] args)
        {
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // 呼び出し先の例外をそのまま上げて、エラーメッセージに実際の原因が出るようにする
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
