namespace ChatWithAI.Contracts
{
    public interface IAdminChecker
    {
        bool IsAdmin(string userId);
    }
}