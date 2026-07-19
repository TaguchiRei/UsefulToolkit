namespace UsefulToolkit.Ai
{
    /// <summary>
    /// AI設定の共通インタフェース。
    /// </summary>
    public interface IAiSettings
    {
        string ClientTypeFullName { get; }
        string ApiKey { get; }
        string ModelName { get; }
        int TimeoutSeconds { get; }
        string SystemPromptSuffix { get; }
        bool EnableAutoExecuteCommands { get; }
        
        void Save();

#if UNITY_EDITOR
        void DrawSettingsGUI();
#endif
    }
}
