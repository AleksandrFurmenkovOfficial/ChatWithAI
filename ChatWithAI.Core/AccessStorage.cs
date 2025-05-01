namespace ChatWithAI.Core
{
    public class AccessStorage(string path) : IAccessStorage
    {
        public Task<string> GetAllowedUsers(CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(path, $"ids.txt");
            if (File.Exists(fullPath))
                return File.ReadAllTextAsync(fullPath, cancellationToken);

            return Task.FromResult("");
        }

        public Task<string> GetPremiumUsers(CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(path, $"premium_ids.txt");
            if (File.Exists(fullPath))
                return File.ReadAllTextAsync(fullPath, cancellationToken);

            return Task.FromResult("");
        }
    }
}
