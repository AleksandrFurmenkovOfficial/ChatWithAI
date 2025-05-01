using File = System.IO.File;

namespace ChatWithAI.Core
{
    public class MemoryStorage(string folderPath) : IMemoryStorage
    {
        private string GetPath(string mode, string chatId) => Path.Combine(folderPath, $"{mode}_{chatId}.txt");

        public Task AddLineContent(string mode, string chatId, string line, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(folderPath))
            {
                _ = Directory.CreateDirectory(folderPath);
            }

            var fullPath = GetPath(mode, chatId);
            return File.AppendAllTextAsync(fullPath, line, cancellationToken);
        }

        public Task<string> GetContent(string mode, string chatId, CancellationToken cancellationToken = default)
        {
            var fullPath = GetPath(mode, chatId);
            if (File.Exists(fullPath))
                return File.ReadAllTextAsync(fullPath, cancellationToken);

            return Task.FromResult("");
        }

        public void Remove(string mode, string chatId, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(folderPath))
                return;

            var fullPath = GetPath(mode, chatId);
            File.Delete(fullPath);
        }
    }
}
