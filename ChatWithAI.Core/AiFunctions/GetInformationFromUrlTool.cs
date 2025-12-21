using HtmlAgilityPack;

namespace ChatWithAI.Core.AiFunctions
{
    /// <summary>
    /// Represents an AI function capable of extracting structured information from a given URL.
    /// </summary>
    public sealed class GetInformationFromUrlTool : IAiFunction
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public GetInformationFromUrlTool(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        // Constants for prompt engineering to ensure consistency and ease of maintenance.
        private const string CONTEXT = @"
// CONTEXT
Your primary role is to meticulously analyze the content of the provided webpage.
You are specialized in the extraction of key facts from news articles, reports, and other text-heavy sources.";

        private const string CORE_INSTRUCTIONS = @"
// CORE INSTRUCTIONS
1.  **Mandatory Sourcing**: For every fact you extract, you MUST provide its direct source URL.
2.  **No Source, No Fact**: If a source URL for a piece of information is not available, you are strictly forbidden from including that information in your response.
3.  **Strict Factual Adherence**: It is imperative that you do not invent, hallucinate, assume, or add any information not explicitly present in the source. Your response must be composed exclusively of data found on the webpage.";

        private const string OUTPUT_FORMAT = @"
// OUTPUT FORMAT
Present each fact individually, immediately followed by its source URL.
For RSS feeds and similar formats, you must use the direct link to the full article, not the raw feed URL.

**Template:**
- Fact: [Extracted fact]
- Source: [Full URL to the source]

**Example:**
- Fact: The weather is clear.
- Temperature: 21°C
- Wind: 15 km/h from the North
- Source: http://example-weather-service.com/city";

        /// <summary>
        /// Gets the unique name of the function.
        /// </summary>
        public string GetName() => nameof(GetInformationFromUrlTool);

        /// <summary>
        /// Provides a detailed description of the function's capabilities and constraints.
        /// </summary>
        public string GetDescription() => $@"{CONTEXT}

{CORE_INSTRUCTIONS}

{OUTPUT_FORMAT}

// RECOMMENDED SOURCES
// The following is a curated list of reliable, text-friendly sources for various types of information.

// 1. Weather
- VATSIM METAR feed: https://metar.vatsim.net/all
- Text-based weather data: http://v2.wttr.in

// 2. Currency and Finance
- Current exchange rates: https://www.xe.com/currencytables
- Cryptocurrency prices: https://www.coindesk.com/price
- Accurate exchange rates: https://openexchangerates.org

// 3. News and Information (Text versions or RSS feeds)
- Kazakhstan: https://tengrinews.kz/news.rss
- Europe: http://ru.euronews.com/rss
- Germany: https://rss.dw.com/xml/rss-ru-all
- United Kingdom: http://feeds.bbci.co.uk/russian/rss.xml
- USA: https://text.npr.org
- Canada: https://www.cbc.ca/lite/
- China: https://www.cgtn.com/subscribe/rss/section/world.xml
- Taiwan: https://feeds.feedburner.com/rsscna/engnews/
- India: https://www.wionews.com/world

// 4. Science and Research
- English Wikipedia: https://en.wikipedia.org/wiki
- arXiv e-print archive: https://arxiv.org/search/?query=YOUR_REQUEST&searchtype=all&abstracts=show&order=-announced_date_first&size=50
- PubMed database: https://pubmed.ncbi.nlm.nih.gov";

        /// <summary>
        /// Defines the parameters required by the function.
        /// </summary>
        public List<Parameter> GetParameters() =>
        [
            new Parameter(
                ParamType.eString,
                "url",
                "The fully qualified URL of the webpage to be analyzed. Prefer simple, text-based pages (like RSS feeds or plain HTML) over pages heavy with client-side scripting.",
                isRequired: true
            ),
            new Parameter(
                ParamType.eString,
                "question",
                "A specific question that guides the information extraction from the webpage.",
                isRequired: true
            )
        ];

        /// <summary>
        /// Executes the AI function to extract information from the given URL.
        /// </summary>
        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            // Parameter validation
            if (!parameters.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("The 'url' parameter is missing or empty.");
            }

            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                throw new ArgumentException("The provided 'url' is not a valid, well-formed URL.");
            }

            if (!parameters.TryGetValue("question", out var question) || string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("The 'question' parameter is missing or empty.");
            }

            // Core logic
            string textContent = await GetTextContentFromUrl(new Uri(url), _httpClientFactory, cancellationToken).ConfigureAwait(false);

            string instruction = $@"{CONTEXT}
{CORE_INSTRUCTIONS}
{OUTPUT_FORMAT}

You must provide an answer based on the facts found in the data from the URL: {url}
If the URL doesn't contain answer, you should use the tools available to you to provide one.";

            var result = await api.GetResponse(userId, instruction, question, textContent, cancellationToken).ConfigureAwait(false);
            return new AiFunctionResult(result!);
        }

        /// <summary>
        /// Fetches and extracts the text content from a given URL.
        /// </summary>
        /// <param name="url">The URL to fetch content from.</param>
        /// <param name="httpClientFactory">The HTTP client factory.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The extracted text content of the webpage.</returns>
        private static async Task<string> GetTextContentFromUrl(Uri url, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
        {
            using (var httpClient = httpClientFactory.CreateClient())
            {
                // Set a user-agent to mimic a standard browser, which can help avoid being blocked.
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                string responseBody = await httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(responseBody);

                // Extracting the inner text of the body is generally more effective than using InnerHtml,
                // as it strips out HTML tags, leaving cleaner text for the AI to process.
                var inner = htmlDocument.DocumentNode.SelectSingleNode("//body")?.InnerText;
                var text = string.IsNullOrEmpty(inner) ? responseBody : inner;

                // Truncate the text to a reasonable length to avoid excessive token usage.
                const int maxTextLength = 420000;
                return text.Length > maxTextLength ? text.Substring(0, maxTextLength) : text;
            }
        }
    }
}