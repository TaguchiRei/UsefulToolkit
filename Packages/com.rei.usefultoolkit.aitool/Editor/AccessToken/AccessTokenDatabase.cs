using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UsefulToolkit.Framework;

namespace UsefulToolkit.Ai
{
    public sealed class AccessTokenDatabase
    {
        private const string TokenChars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        private static AccessTokenDatabase instance;

        public static AccessTokenDatabase Instance => instance ??= new AccessTokenDatabase();

        [Serializable]
        private class Entry
        {
            public string token;
            public string value;
        }

        [Serializable]
        private class DatabaseData
        {
            public List<Entry> entries = new();
        }

        private readonly Dictionary<string, string> tokenToValue = new();
        private readonly Dictionary<string, string> valueToToken = new();

        private readonly string databasePath;

        private AccessTokenDatabase()
        {
            databasePath = Path.Combine(GetDatabaseDirectory(), "tokens.json");

            Load();
        }

        public string GetOrCreateToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(nameof(value));

            if (valueToToken.TryGetValue(value, out string existing)) return existing;

            string token = GenerateUniqueToken();

            valueToToken[value] = token;
            tokenToValue[token] = value;

            Save();

            return token;
        }

        public bool TryResolveToken(string token, out string value)
        {
            return tokenToValue.TryGetValue(token, out value);
        }

        public bool ContainsToken(string token)
        {
            return tokenToValue.ContainsKey(token);
        }

        public bool RemoveToken(string token)
        {
            if (!tokenToValue.TryGetValue(token, out string value))
            {
                return false;
            }

            tokenToValue.Remove(token);
            valueToToken.Remove(value);

            Save();

            return true;
        }

        private void Load()
        {
            tokenToValue.Clear();
            valueToToken.Clear();

            if (!File.Exists(databasePath)) return;

            string json = File.ReadAllText(databasePath);

            DatabaseData data = JsonUtility.FromJson<DatabaseData>(json);

            if (data == null) return;

            foreach (Entry entry in data.entries)
            {
                if (string.IsNullOrEmpty(entry.token)) continue;

                tokenToValue[entry.token] = entry.value;
                valueToToken[entry.value] = entry.token;
            }
        }

        private void Save()
        {
            DatabaseData data = new();

            foreach (var pair in tokenToValue)
            {
                data.entries.Add(new Entry
                {
                    token = pair.Key,
                    value = pair.Value
                });
            }

            string json = JsonUtility.ToJson(data, true);

            FileGenerator.WriteFile(databasePath, json);
        }

        private string GenerateUniqueToken()
        {
            string token;

            do
            {
                token = GenerateToken(12);
            } while (tokenToValue.ContainsKey(token));

            return token;
        }

        private static string GenerateToken(int length)
        {
            char[] chars = new char[length];

            for (int i = 0; i < length; i++)
            {
                chars[i] = TokenChars[
                    UnityEngine.Random.Range(
                        0,
                        TokenChars.Length)];
            }

            return new string(chars);
        }

        private static string GetDatabaseDirectory()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "UserSettings", "AIAccess");
        }
    }
}