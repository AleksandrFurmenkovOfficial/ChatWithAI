namespace ChatWithAI.Core
{
    /// <summary>
    /// Interface for content helper operations that require HTTP requests.
    /// </summary>
    public interface IContentHelper
    {
        /// <summary>
        /// Encodes an image from a URL to WebP Base64 format.
        /// Returns null if the file is unavailable (404, 401, etc.).
        /// </summary>
        Task<string?> EncodeImageToWebpBase64(Uri imageUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a stream from a URL.
        /// Returns null if the file is unavailable (404, 401, etc.).
        /// </summary>
        Task<Stream?> GetStreamFromUrlAsync(Uri url, CancellationToken cancellationToken = default);

        /// <summary>
        /// Encodes audio from a URL to Base64 format.
        /// Returns null if the file is unavailable (404, 401, etc.).
        /// </summary>
        Task<string?> EncodeAudioToBase64(Uri audioUrl, CancellationToken cancellationToken = default);

        /// <summary>
        /// Encodes a file from a URL to Base64 format.
        /// Returns null if the file is unavailable (404, 401, etc.).
        /// </summary>
        Task<string?> EncodeFileToBase64(Uri fileUrl, CancellationToken cancellationToken = default);
    }
}
