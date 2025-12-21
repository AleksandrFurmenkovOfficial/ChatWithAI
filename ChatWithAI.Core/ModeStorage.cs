using System.Text;

namespace ChatWithAI.Core
{
    public class ModeStorage(string path) : IModeStorage
    {
        private readonly string _basePath = !string.IsNullOrWhiteSpace(path)
            ? path
            : throw new ArgumentException("Path cannot be empty", nameof(path));

        public async Task<string> GetContent(string modeName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modeName))
                return string.Empty;

            var safeName = Path.GetFileName(modeName);
            var fullPath = Path.Combine(_basePath, $"{safeName}.txt");

            try
            {
                return await File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
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