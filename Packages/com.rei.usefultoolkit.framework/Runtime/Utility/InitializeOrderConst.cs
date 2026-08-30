using UnityEngine;

namespace UsefulToolkit.Utility
{
    public static class InitializeOrderConst
    {
        public const int Compositor = -100;
        public const int InitializerEarly = -80;
        public const int Initializer = -70;
        public const int InitializerLate = -60;
        public const int DefaultEarly = -10;
        public const int Default = 0;
        public const int DefaultLate = 10;
    }
}