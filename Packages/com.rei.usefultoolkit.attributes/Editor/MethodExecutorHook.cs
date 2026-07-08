using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Attributes
{
    using Debug = UnityEngine.Debug;

    [InitializeOnLoad]
    internal static class MethodExecutorHook
    {
        static MethodExecutorHook()
        {
            Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
        }

        private static void OnPostHeaderGUI(Editor editor)
        {
            if (editor == null)
            {
                return;
            }

            foreach (var target in editor.targets)
            {
                if (target == null)
                {
                    continue;
                }

                InspectorButtonDrawer.Draw(target);
            }
        }
    }

    internal static class InspectorButtonDrawer
    {
        private static readonly Dictionary<Type, MethodData[]> CachedMethods = new();

        internal static void Draw(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            var methods = GetMethods(target.GetType());

            if (methods.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space();

            foreach (var methodData in methods)
            {
                bool canExecute =
                    Application.isPlaying ||
                    methodData.Attribute.CanExecuteInEditMode;

                using (new EditorGUI.DisabledScope(!canExecute))
                {
                    if (GUILayout.Button(methodData.Attribute.ButtonName))
                    {
                        ExecuteMethod(target, methodData.Method);
                    }
                }

                if (!canExecute)
                {
                    EditorGUILayout.HelpBox(
                        $"{methodData.Method.Name} はランタイム中のみ実行できます",
                        MessageType.Info);
                }
            }
        }

        private static MethodData[] GetMethods(Type type)
        {
            if (CachedMethods.TryGetValue(type, out var methods))
            {
                return methods;
            }

            var methodList = new List<MethodData>();

            var methodInfos = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            foreach (var method in methodInfos)
            {
                var attr = method.GetCustomAttribute<MethodExecutorAttribute>();

                if (attr == null)
                {
                    continue;
                }

                if (!IsValidMethod(method))
                {
                    continue;
                }

                methodList.Add(new MethodData(method, attr));
            }

            methods = methodList.ToArray();
            CachedMethods[type] = methods;

            return methods;
        }

        private static bool IsValidMethod(MethodInfo method)
        {
            if (method.GetParameters().Length > 0)
            {
                return false;
            }

            if (method.IsGenericMethod)
            {
                return false;
            }

            if (method.IsAbstract)
            {
                return false;
            }

            if (method.IsStatic)
            {
                return false;
            }

            return true;
        }

        private static void ExecuteMethod(UnityEngine.Object target, MethodInfo method)
        {
            Undo.RecordObject(target, method.Name);

            try
            {
                method.Invoke(target, null);

                EditorUtility.SetDirty(target);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private readonly struct MethodData
        {
            internal readonly MethodInfo Method;
            internal readonly MethodExecutorAttribute Attribute;

            internal MethodData(
                MethodInfo method,
                MethodExecutorAttribute attribute)
            {
                Method = method;
                Attribute = attribute;
            }
        }
    }
}