using System;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public sealed class AiChatSettings : SettingsBase<AiChatSettings>
    {
        [SerializeReference, SubclassSelector]
        public IAiSettings ActiveClientSettings;
    }
}