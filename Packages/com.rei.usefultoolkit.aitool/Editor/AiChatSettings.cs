using System;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.Editor.Setting;

namespace UsefulToolkit.Editor.Ai
{
    [Serializable]
    public sealed class AiChatSettings : SettingsBase<AiChatSettings>
    {
        [SerializeReference, SubclassSelector]
        public IAiSettings ActiveClientSettings;

        public string PlanningDocumentsPath = "LocalAssets/UsefulToolkit/AITool/PlanningDocuments";
    }
}