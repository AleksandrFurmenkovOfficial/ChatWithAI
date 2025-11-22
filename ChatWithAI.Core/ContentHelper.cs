namespace ChatWithAI.Core
{
    /// <summary>
    /// Implementation of content helper operations using IHttpClientFactory.
    /// </summary>
    public sealed class ContentHelper(IHttpClientFactory httpClientFactory, ILogger logger) : IContentHelper
    {
        private static readonly int[] RetryDelaysMs = [1000, 2000, 3000];
        private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task<string?> EncodeImageToWebpBase64(Uri imageUrl, CancellationToken cancellationToken = default)
        {
            var imageBytes = await DownloadWithRetryAsync(imageUrl, cancellationToken).ConfigureAwait(false);
            if (imageBytes == null)
            {
                return null;
            }

            return Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(imageBytes));
        }

        public async Task<Stream?> GetStreamFromUrlAsync(Uri url, CancellationToken cancellationToken = default)
        {
            var bytes = await DownloadWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
            if (bytes == null)
            {
                return null;
            }

            return new MemoryStream(bytes);
        }

        public async Task<string?> EncodeAudioToBase64(Uri audioUrl, CancellationToken cancellationToken = default)
        {
            return await EncodeFileToBase64(audioUrl, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string?> EncodeFileToBase64(Uri fileUrl, CancellationToken cancellationToken = default)
        {
            var fileBytes = await DownloadWithRetryAsync(fileUrl, cancellationToken).ConfigureAwait(false);
            if (fileBytes == null)
            {
                return null;
            }

            return Convert.ToBase64String(fileBytes);
        }

        private async Task<byte[]?> DownloadWithRetryAsync(Uri url, CancellationToken cancellationToken)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            HttpRequestException? lastException = null;

            for (int attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
            {
                try
                {
                    return await httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;

                    if (attempt < RetryDelaysMs.Length)
                    {
                        _logger.LogInfoMessage($"Download attempt {attempt + 1} failed for {url}: {ex.Message}. Retrying in {RetryDelaysMs[attempt]}ms...");
                        await Task.Delay(RetryDelaysMs[attempt], cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Log as info since expired Telegram file links are expected behavior
            _logger.LogInfoMessage($"Failed to download {url} after {RetryDelaysMs.Length + 1} attempts: {lastException?.Message}");
            return null;
        }
    }
}
