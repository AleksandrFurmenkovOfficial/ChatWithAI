using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace ChatWithAI.Core
{
    public class AccessChecker
    {
        private readonly IAdminChecker _adminChecker;
        private readonly IAccessStorage _accessStorage;
        private readonly ConcurrentDictionary<string, AppVisitor> _visitorByChatId;

        private readonly Lazy<Task<AccessData>> _dataLoader;

        public AccessChecker(
            IAdminChecker adminChecker,
            ConcurrentDictionary<string, AppVisitor> visitorByChatId,
            IAccessStorage accessStorage)
        {
            _adminChecker = adminChecker;
            _visitorByChatId = visitorByChatId;
            _accessStorage = accessStorage;

            _dataLoader = new Lazy<Task<AccessData>>(LoadDataAsync);
        }

        public async Task<bool> HasAccessAsync(string chatId, string username)
        {
            var data = await _dataLoader.Value.ConfigureAwait(false);

            var visitor = _visitorByChatId.GetOrAdd(chatId, id =>
            {
                bool accessByDefault = data.AllowedUsers.Contains(chatId) ||
                                     _adminChecker.IsAdmin(chatId);
                return new AppVisitor(accessByDefault, username, DateTime.UtcNow);
            });

            visitor.LatestAccess = DateTime.UtcNow;
            return visitor.Access;
        }

        public async Task<bool> IsPremiumUserAsync(string chatId)
        {
            var data = await _dataLoader.Value.ConfigureAwait(false);
            return data.PremiumUsers.Contains(chatId);
        }

        private async Task<AccessData> LoadDataAsync()
        {
            var usersRaw = await _accessStorage.GetAllowedUsers().ConfigureAwait(false);
            var premiumRaw = await _accessStorage.GetPremiumUsers().ConfigureAwait(false);

            var allowed = ParseIds(usersRaw);
            var premium = ParseIds(premiumRaw);

            return new AccessData(allowed.ToFrozenSet(), premium.ToFrozenSet());
        }

        private static IEnumerable<string> ParseIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) yield break;

            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return line.Trim();
            }
        }

        private sealed record AccessData(FrozenSet<string> AllowedUsers, FrozenSet<string> PremiumUsers);
    }
}