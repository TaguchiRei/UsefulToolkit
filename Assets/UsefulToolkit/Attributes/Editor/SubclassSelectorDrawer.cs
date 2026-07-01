using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UsefulToolkit.Attributes
{
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public class SubclassSelectorDrawer : PropertyDrawer
    {
        private static readonly
            Dictionary<string, (Type[] inheritedTypes, string[] typePopupNameArray, string[] typeFullNameArray)>
            typeCache = new();

        bool initialized = false;
        Type[] inheritedTypes;
        string[] typePopupNameArray;
        string[] typeFullNameArray;
        int currentTypeIndex;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference) return;
            if (!initialized)
            {
                Initialize(property);
                GetCurrentTypeIndex(property.managedReferenceFullTypename);
                initialized = true;
            }

            
            const int maxDepth = 10; 
            bool includeChildren = EditorGUI.indentLevel < maxDepth;

            int selectedTypeIndex = EditorGUI.Popup(GetPopupPosition(position), currentTypeIndex, typePopupNameArray);
            UpdatePropertyToSelectedTypeIndex(property, selectedTypeIndex);
            
            EditorGUI.PropertyField(position, property, label, includeChildren);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, true);
        }

        private void Initialize(SerializedProperty property)
        {
            SubclassSelectorAttribute utility = (SubclassSelectorAttribute)attribute;
            string baseTypeIdentifier = property.managedReferenceFieldTypename;
            
            if (string.IsNullOrEmpty(baseTypeIdentifier))
            {
                inheritedTypes = new Type[] { null };
                typePopupNameArray = new string[] { "<error: base type not found>" };
                typeFullNameArray = new string[] { "" };
                return;
            }

            if (typeCache.TryGetValue(baseTypeIdentifier, out var cache))
            {
                inheritedTypes = cache.inheritedTypes;
                typePopupNameArray = cache.typePopupNameArray;
                typeFullNameArray = cache.typeFullNameArray;
            }
            else
            {
                Type baseType = GetType(property);
                if (baseType == null)
                {
                    inheritedTypes = new Type[] { null };
                    typePopupNameArray = new string[] { "<error: base type not found>" };
                    typeFullNameArray = new string[] { "" };
                    return;
                }

                GetAllInheritedTypes(baseType, utility.IsIncludeMono());
                GetInheritedTypeNameArrays();
                typeCache[baseTypeIdentifier] = (inheritedTypes, typePopupNameArray, typeFullNameArray);
            }
        }

        private void GetCurrentTypeIndex(string typeFullName)
        {
            currentTypeIndex = Array.IndexOf(typeFullNameArray, typeFullName);
        }

        void GetAllInheritedTypes(Type baseType, bool includeMono)
        {
            Type monoType = typeof(MonoBehaviour);
            inheritedTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => baseType.IsAssignableFrom(p) && p.IsClass && !p.IsAbstract &&
                            (!monoType.IsAssignableFrom(p) || includeMono))
                .Prepend(null)
                .ToArray();
        }

        private void GetInheritedTypeNameArrays()
        {
            typePopupNameArray = inheritedTypes.Select(type => type == null ? "<null>" : type.ToString()).ToArray();
            typeFullNameArray = inheritedTypes.Select(type =>
                    type == null ? "" : $"{type.Assembly.ToString().Split(',')[0]} {type.FullName}")
                .ToArray();
        }

        public void UpdatePropertyToSelectedTypeIndex(SerializedProperty property, int selectedTypeIndex)
        {
            if (currentTypeIndex == selectedTypeIndex) return;
            currentTypeIndex = selectedTypeIndex;
            Type selectedType = inheritedTypes[selectedTypeIndex];
            property.managedReferenceValue =
                selectedType == null ? null : Activator.CreateInstance(selectedType);
        }

        Rect GetPopupPosition(Rect currentPosition)
        {
            Rect popupPosition = new Rect(currentPosition);
            popupPosition.width -= EditorGUIUtility.labelWidth;
            popupPosition.x += EditorGUIUtility.labelWidth;
            popupPosition.height = EditorGUIUtility.singleLineHeight;
            return popupPosition;
        }
        
        public static Type GetType(SerializedProperty property)
        {
            string[] typeNameParts = property.managedReferenceFieldTypename.Split(' ');
            if (typeNameParts.Length == 2)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == typeNameParts[0]);
                if (assembly != null)
                {
                    return assembly.GetType(typeNameParts[1]);
                }
            }

            return null;
        }
    }
}