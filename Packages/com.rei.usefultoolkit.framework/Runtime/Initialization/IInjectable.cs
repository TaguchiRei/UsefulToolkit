namespace UsefulToolkit.Initialization
{
    public interface IInjectable<in T>
    {
        void Inject(T instance);
    }

    public interface IInjectable<in T1, in T2>
    {
        void Inject(T1 instance1, T2 instance2);
    }

    public interface IInjectable<in T1, in T2, in T3>
    {
        void Inject(T1 instance1, T2 instance2, T3 instance3);
    }

    public interface IInjectable<in T1, in T2, in T3, in T4>
    {
        void Inject(T1 instance1, T2 instance2, T3 instance3, T4 instance4);
    }
}
