using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ChatWithAI.Providers.Google
{
    public class Imagegen4AiImagePainter : IAiImagePainter
    {
        private readonly string _apiKey;
        private readonly IHttpClientFactory _httpClientFactory;
        private const string _modelName = "imagen-4.0-ultra-generate-preview-06-06";
        private const string _baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

        public Imagegen4AiImagePainter(string apiKey, IHttpClientFactory httpClientFactory)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
            }
            _apiKey = apiKey;
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        private async Task<ImageContentItem> GetImageWithReTry(string imageDescription, string imageSize, string userId, int reTryCount, CancellationToken cancellationToken)
        {
            try
            {
                imageDescription = imageDescription.Replace("young", "a first-year student age");
                imageDescription = imageDescription.Replace("\u0027", "");
                imageDescription = imageDescription.Replace("'", "");
                imageDescription = imageDescription.Replace("  ", " ");

                using (var client = _httpClientFactory.CreateClient("google_gemini_client"))
                {
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    // --- Parameter Handling (from Notebook Cell) ---
                    //  We'll use the parameters passed to GetImage, but I'll show how to override
                    //  them for testing, mirroring the notebook's behavior.

                    // 1. prompt (imageDescription):  Use the value passed to the method.
                    string prompt = imageDescription;

                    // 2. sampleCount: Use the parameter, but allow for testing overrides.
                    int sampleCount = 1;  // Default to 1, as per IAiImagePainter interface.
                    // *For Testing*:  Uncomment to override, like the notebook cell.
                    // sampleCount = 4;

                    // 3. aspectRatio (imageSize): Use the value passed to the method, validated below.
                    string aspectRatio = imageSize;
                    // Validate aspectRatio (important for robustness).
                    string[] validAspectRatios = { "1:1", "3:4", "4:3", "16:9", "9:16" };
                    if (!Array.Exists(validAspectRatios, element => element == aspectRatio))
                    {
                        throw new ArgumentException("Invalid aspectRatio. Must be one of: 1:1, 3:4, 4:3, 16:9, 9:16", nameof(imageSize));
                    }

                    // 4. personGeneration:  Default, but allow for testing overrides.
                    //string personGeneration = "allow_adult";
                    // *For Testing*: Uncomment to override.
                    string personGeneration = "allow_adult";

                    // --- Request Construction ---
                    var requestData = new
                    {
                        instances = new[]
                        {
                            new { prompt }
                        },
                        parameters = new
                        {
                            sampleCount, // Use the potentially overridden value
                            aspectRatio,  // Use the potentially overridden value
                            personGeneration, // Use the potentially overridden value
                        }
                    };

                    var jsonContent = JsonSerializer.Serialize(requestData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    Debug.WriteLine($"Request JSON: {jsonContent}");

                    string fullUrl = $"{_baseUrl}{_modelName}:predict?key={_apiKey}";
                    var response = await client.PostAsync(fullUrl, content, cancellationToken).ConfigureAwait(false);

                    Debug.WriteLine($"Response Status Code: {response.StatusCode}");
                    foreach (var header in response.Headers)
                    {
                        Debug.WriteLine($"Response Header: {header.Key} = {string.Join(", ", header.Value)}");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        Debug.WriteLine($"Error Response: {errorResponse}");
                        throw new HttpRequestException($"API request failed with status code: {response.StatusCode}, Error: {errorResponse}");
                    }

                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                    string responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    Debug.WriteLine($"Response Content: {responseContent}");

                    using (JsonDocument doc = JsonDocument.Parse(responseContent))
                    {
                        if (doc.RootElement.TryGetProperty("predictions", out JsonElement predictions))
                        {
                            if (predictions.GetArrayLength() > 0)
                            {
                                JsonElement firstPrediction = predictions[0];
                                if (firstPrediction.TryGetProperty("bytesBase64Encoded", out JsonElement base64Encoded))
                                {
                                    string base64String = base64Encoded.GetString()!;
                                    byte[] pngImageBytes = Convert.FromBase64String(base64String);
                                    return new ImageContentItem { ImageUrl = null, ImageInBase64 = Convert.ToBase64String(Helpers.ConvertImageBytesToWebp(pngImageBytes)) };
                                }
                                else
                                {
                                    if (firstPrediction.TryGetProperty("raiFilteredReason", out JsonElement raiReason))
                                    {
                                        throw new InvalidOperationException($"Image generation was blocked by RAI filtering. Reason: {raiReason.GetString()}");
                                    }
                                    if (firstPrediction.TryGetProperty("safetyAttributes", out JsonElement safetyAttributes))
                                    {
                                        Debug.WriteLine($"Safety Attributes: {safetyAttributes}");
                                        throw new InvalidOperationException("Image generation was blocked due to safety concerns. See safety attributes for details.");

                                    }

                                    throw new InvalidOperationException("Response did not contain image data (bytesBase64Encoded).");
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("No image generated: The 'predictions' array is empty.");
                            }
                        }
                        else
                        {
                            if (reTryCount < 7)
                            {
                                await Task.Delay(reTryCount * 254, cancellationToken).ConfigureAwait(false);
                                return await GetImageWithReTry(imageDescription, imageSize, userId, reTryCount + 1, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                throw new InvalidOperationException("No image generated: The 'predictions' property is missing from the response. Check for quota/billing issues or API errors.");
                            }
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"HTTP Request Error: {ex.Message}");
                throw;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Json Deserialization Error: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An unexpected error occurred: {ex.Message}");
                throw;
            }
        }

        public Task<ImageContentItem> GetImage(string imageDescription, string imageSize, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(imageDescription))
            {
                throw new ArgumentException("Image description cannot be null or empty.", nameof(imageDescription));
            }
            if (string.IsNullOrEmpty(imageSize))
            {
                throw new ArgumentException("Image size cannot be null or empty.", nameof(imageSize));
            }

            return GetImageWithReTry(imageDescription, imageSize, userId, 0, cancellationToken);
        }
    }
}