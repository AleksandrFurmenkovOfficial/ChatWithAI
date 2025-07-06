using ChatWithAI.Core.ChatCommands;
using System.Collections.Concurrent;

namespace ChatWithAI.Core
{
    public class AccessChecker(IAdminChecker adminChecker, ConcurrentDictionary<string, IAppVisitor> visitorByChatId, IAccessStorage accessStorage)
    {
        private readonly HashSet<string> allowed = [];
        private readonly HashSet<string> premium = [];
        private int isInitialized;

        public bool HasAccess(string chatId, string username)
        {
            if (Interlocked.Exchange(ref isInitialized, 1) == 0)
                Initialize();

            var visitor = visitorByChatId.GetOrAdd(chatId, id =>
            {
                bool accessByDefault = allowed.Contains(chatId) || adminChecker.IsAdmin(chatId);
                return new AppVisitor(accessByDefault, username, DateTime.UtcNow);
            });

            visitor.LatestAccess = DateTime.UtcNow;
            return visitor.Access;
        }

        public bool IsPremiumUser(string chatId)
        {
            if (Interlocked.Exchange(ref isInitialized, 1) == 0)
                Initialize();

            return premium.Contains(chatId);
        }

        private void Initialize()
        {
            var users = accessStorage.GetAllowedUsers().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(users))
            {
                foreach (var id in users.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    allowed.Add(id.Trim());
                }
            }

            var premiumUsers = accessStorage.GetPremiumUsers().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(premiumUsers))
            {
                foreach (var id in premiumUsers.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    premium.Add(id.Trim());
                }
            }
        }
    }
}