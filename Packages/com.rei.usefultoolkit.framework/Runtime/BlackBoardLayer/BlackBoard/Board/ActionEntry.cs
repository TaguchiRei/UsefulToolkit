using System;
using UnityEngine;

namespace UsefulToolkit.BlackBoard
{
    public struct ActionEntry
    {
        public readonly bool _disposeOnUsed;
        private Action _action;

        public ActionEntry(bool disposeOnUsed, Action action)
        {
            _disposeOnUsed = disposeOnUsed;
            _action = action;
        }
    }
}