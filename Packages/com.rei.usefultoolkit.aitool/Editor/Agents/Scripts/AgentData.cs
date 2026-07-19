using UnityEngine;
using UsefulToolkit.Attributes;

namespace UsefulToolkit.Ai
{
    [CreateAssetMenu(fileName = "AgentData", menuName = "UsefulToolkit/Ai/AgentData")]
    public class AgentData : ScriptableObject
    {
        public string Name;
        public string Role;
        [TextArea] public string SystemPrompt;
        public Texture2D Icon;

        [SerializeReference, SubclassSelector] private ISetCommand[] _setCommands;
        [SerializeReference, SubclassSelector] private IGetCommand[] _getCommands;
        [SerializeReference, SubclassSelector] private IContactCommand[] _contactCommands;

        public ISetCommand[] SetCommands => _setCommands;
        public IGetCommand[] GetCommands => _getCommands;
        public IContactCommand[] ContactCommands => _contactCommands;


        [MethodExecutor("アイコンの形状を整える", true)]
        public void CreateIcon()
        {
            CircularIconGeneratorWindow.Open(Icon, icon => { Icon = icon; });
        }
    }
}