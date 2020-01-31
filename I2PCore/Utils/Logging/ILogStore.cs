namespace I2PCore.Utils
{
    public interface ILogStore
    {
        string Name { set; get; }
        void Log( string text );
        void CheckStoreRotation();
        void Close();
    }
}
