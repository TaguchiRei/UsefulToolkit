using UnityEngine;
using UsefulToolkit.BlackBoard.BlackBoard;

namespace UsefulToolkit.BlackBoard.ProgramTools
{
    public interface IPausable
    {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}