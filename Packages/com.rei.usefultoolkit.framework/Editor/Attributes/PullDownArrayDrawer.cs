using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Attributes
{
    [CustomPropertyDrawer(typeof(PullDownArrayAttribute))]
    public class PullDownArrayDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (PullDownArrayAttribute)attribute;
            var target = property.serializedObject.targetObject;

            FieldInfo field = target.GetType().GetField(
                attr.MemberName,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static);

            if (field == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            if (field.GetValue(null) is not Array values || values.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            string[] displayNames = values
                .Cast<object>()
                .Select(x => x?.ToString() ?? "null")
                .ToArray();

            int index = GetCurrentIndex(property, values);

            EditorGUI.BeginProperty(position, label, property);

            int newIndex = EditorGUI.Popup(position, label.text, index, displayNames);

            if (newIndex != index)
            {
                SetValue(property, values.GetValue(newIndex));
            }

            EditorGUI.EndProperty();
        }

        private static int GetCurrentIndex(SerializedProperty property, Array values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (Equals(values.GetValue(i), GetValue(property)))
                    return i;
            }

            return 0;
        }

        private static object GetValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Enum => property.enumValueIndex,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue,
                _ => null
            };
        }

        private static void SetValue(SerializedProperty property, object value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    property.stringValue = value?.ToString();
                    break;

                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(value);
                    break;

                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(value);
                    break;

                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(value);
                    break;

                case SerializedPropertyType.Enum:
                    property.enumValueIndex = Convert.ToInt32(value);
                    break;

                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value as UnityEngine.Object;
                    break;
            }
        }
    }
}