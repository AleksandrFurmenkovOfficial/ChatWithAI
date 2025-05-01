namespace ChatWithAI.Core
{
    public class ModeStorage(string path) : IModeStorage
    {
        public Task<string> GetContent(string modeName, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.Combine(path, $"{modeName}.txt");
            if (File.Exists(fullPath))
                return File.ReadAllTextAsync(fullPath, cancellationToken);

            return Task.FromResult("");
        }
    }
}
