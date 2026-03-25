namespace ChatWithAI.Core
{
    public class AccessStorage(string path) : IAccessStorage
    {
        public async Task<string> GetAllowedUsers(CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(path, "ids.txt");
            try
            {
                return await File.ReadAllTextAsync(fullPath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
        }

        public async Task<string> GetPremiumUsers(CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(path, "premium_ids.txt");
            try
            {
                return await File.ReadAllTextAsync(fullPath, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return string.Empty;
            }
            catch (DirectoryNotFoundException)
            {
                return string.Empty;
            }
        }
    }
}