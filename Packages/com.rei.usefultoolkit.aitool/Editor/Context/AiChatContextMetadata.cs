using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class AiChatContextItem
    {
        public string Id;
        public string Name;
        public long LastModified;

        public AiChatContextItem() { }
        public AiChatContextItem(string id, string name)
        {
            Id = id;
            Name = name;
            LastModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public override string ToString() => Name;
    }

    [Serializable]
    public class AiChatContextMetadata
    {
        public List<AiChatContextItem> Contexts = new List<AiChatContextItem>();
        public string CurrentContextId;

        private static string SavePath => "UserSettings/UsefulToolkit/AiChat/Metadata.json";

        public void Save()
        {
            var json = JsonUtility.ToJson(this, true);
            FileGenerator.WriteFile(SavePath, json);
        }

        public static AiChatContextMetadata Load()
        {
            if (!File.Exists(SavePath)) return new AiChatContextMetadata();
            try
            {
                var json = File.ReadAllText(SavePath);
                return JsonUtility.FromJson<AiChatContextMetadata>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AiChatContextMetadata] Failed to load metadata: {e.Message}");
                return new AiChatContextMetadata();
            }
        }
    }
}
