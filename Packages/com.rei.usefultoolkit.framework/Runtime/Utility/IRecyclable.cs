namespace UsefulToolkit.Utility
{
    /// <summary> RecycleBufferで再利用管理されるオブジェクトが実装するインターフェース。 </summary>
    public interface IRecyclable
    {
        int RecycleId { get; set; }
        void OnRecycle();
    }
}
