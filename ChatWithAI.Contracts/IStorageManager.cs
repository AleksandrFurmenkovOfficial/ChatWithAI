namespace ChatWithAI.Contracts
{
    public interface IStorageManager
    {
        IAccessStorage GetAccessStorage();
        IModeStorage GetModeStorage();
        IMemoryStorage GetMemoryStorage();
    }
}