using System.Text;

namespace ChatWithAI.Core
{
    public class MemoryStorage : IMemoryStorage
    {
        private readonly string _folderPath;

        public MemoryStorage(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Path cannot be empty", nameof(folderPath));

            _folderPath = folderPath;

            Directory.CreateDirectory(_folderPath);
        }

        private string GetPath(string mode, string chatId)
        {
            var safeMode = Path.GetFileName(mode);
            var safeChatId = Path.GetFileName(chatId);
            return Path.Combine(_folderPath, $"{safeMode}_{safeChatId}.txt");
        }

        public async Task AddLineContent(string chatId, string mode, string line, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPath(mode, chatId);
            await File.AppendAllTextAsync(fullPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken);
        }

        public async Task<string> GetContent(string chatId, string mode, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPath(mode, chatId);
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

        public void Remove(string chatId, string mode, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPath(mode, chatId);
            File.Delete(fullPath);
        }
    }
}