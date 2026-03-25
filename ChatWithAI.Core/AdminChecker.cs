namespace ChatWithAI.Core
{
    public sealed class AdminChecker(string adminUserId) : IAdminChecker
    {
        private readonly string _adminUserId = !string.IsNullOrWhiteSpace(adminUserId)
            ? adminUserId
            : throw new ArgumentException("Admin User ID cannot be null or empty.", nameof(adminUserId));

        public bool IsAdmin(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            return string.Equals(userId, _adminUserId, StringComparison.OrdinalIgnoreCase);
        }
    }
}