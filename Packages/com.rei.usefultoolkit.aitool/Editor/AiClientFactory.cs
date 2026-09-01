using System;
using System.Linq;

namespace UsefulToolkit.Editor.Ai
{
    public static class AiClientFactory
    {
        public static IAiClient CreateClient(string typeFullName, string apiKey, string modelName, string systemPrompt, int timeoutSeconds)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == typeFullName);

            if (type == null)
                throw new NotSupportedException($"AI Type {typeFullName} is not found.");

            try
            {
                var client = (IAiClient)Activator.CreateInstance(type, apiKey, modelName, systemPrompt);
                client.TimeoutSeconds = timeoutSeconds;
                return client;
            }
            catch (MissingMethodException ex)
            {
                throw new MissingMethodException(
                    $"Constructor on type '{typeFullName}' with arguments (string, string, string) not found. Please ensure {type.Name} has a constructor accepting (string apiKey, string modelName, string systemPrompt).", ex);
            }
        }
    }
}
