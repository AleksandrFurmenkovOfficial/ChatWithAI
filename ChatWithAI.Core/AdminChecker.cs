namespace ChatWithAI.Core
{
    public sealed class AdminChecker(string adminUserId) : IAdminChecker
    {
        public bool IsAdmin(string userId) => string.Equals(userId, adminUserId, StringComparison.OrdinalIgnoreCase);
    }
}