using System;
using System.Linq;

namespace UsefulToolkit.Ai
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

            // IAiClientを実装している型に対してインスタンス化を試みる
            // コンストラクタ引数が異なる場合への対応が課題だが、
            // 汎用的なIAiClient生成として、一旦Activatorで生成し、
            // プロパティへのセットを行う。
            var client = (IAiClient)Activator.CreateInstance(type, apiKey, modelName, systemPrompt);
            client.TimeoutSeconds = timeoutSeconds;
            
            return client;
        }
    }
}
