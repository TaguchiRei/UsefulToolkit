using UnityEngine;

namespace UsefulToolkit.ProgramTools
{
    public interface IPausable
    {
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}