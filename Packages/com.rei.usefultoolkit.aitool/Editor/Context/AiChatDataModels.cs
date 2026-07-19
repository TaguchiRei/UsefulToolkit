using System;
using System.Collections.Generic;

namespace UsefulToolkit.Ai
{
    [Serializable]
    public class AiResponse
    {
        public string message;
        public string intent;
        public CommandInfo[] commands;
    }

    [Serializable]
    public class CommandInfo
    {
        public string name;
        public string[] arguments;
        [NonSerialized] public bool isPending = true;
        [NonSerialized] public bool isApproved = false;
        [NonSerialized] public string executionResult = "";
    }

    public class ParsedMessage
    {
        public string Role;
        public string Text;
        public List<CommandInfo> Commands = new List<CommandInfo>();
    }
}
