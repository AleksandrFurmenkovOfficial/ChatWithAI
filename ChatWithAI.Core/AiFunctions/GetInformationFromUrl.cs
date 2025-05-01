using HtmlAgilityPack;

namespace ChatWithAI.Core.AiFunctions
{
    public sealed class GetInformationFromUrl : IAiFunction
    {
        public string GetName()
        {
            return nameof(GetInformationFromUrl);
        }

        public string GetDescription()
        {
            return
@"This function retrieves and analyzes the content of specified webpages, focusing on simple, text-based sites. It's particularly effective for extracting news and other straightforward data from minimalistic web pages.

Function rating: 10 out of 10

Key Features:
- Extracts data from simple, text-based websites
- Avoids complex, heavily scripted pages
- Ideal for basic HTML layouts without interactive elements

Web Resource Guide:
1. Weather:
   - http://v2.wttr.in - Version 2 with more detailed text-based weather data
   - https://weather.gc.ca/text_forecast_e.html - Text-based weather forecasts from Environment Canada

2. Time and Date:
   - https://time.is - Current time information
   - https://www.timeanddate.com - Comprehensive time and date data

3. Currency and Finance:
   - https://www.xe.com/currencytables/ - Clean format for current exchange rates
   - https://www.coindesk.com/price/ - Latest cryptocurrency prices
   - https://openexchangerates.org - Minimalistic yet accurate exchange rates

4. News and Information:
   - https://text.npr.org - Text-only version of NPR news
   - https://lite.cnn.com - Lightweight version of CNN

5. Network and System Information:
   - https://icanhazip.com - Simple display of your public IP address
   - https://ifconfig.me - Network configuration details

6.Science and information:
   - https://en.wikipedia.org/wiki/ - English wikipedia
   - https://pubmed.ncbi.nlm.nih.gov/ - Free database including primarily the MEDLINE database of references and abstracts on life sciences and biomedical topics";
        }

        public List<Parameter> GetParameters()
        {
            return
            [
                new Parameter(
                    ParamType.eString,
                    "url",
                    "The URL of the webpage to be analyzed. Prefer simple web pages with plain text data over complex URLs loaded with scripts and other elements.",
                    true
                ),
                new Parameter(
                    ParamType.eString,
                    "question",
                    "A question regarding the information to be extracted from the webpage.",
                    true
                )
            ];
        }

        private static async Task<string> GetTextContentOnly(Uri url, CancellationToken cancellationToken = default)
        {
            using (var httpClient = new HttpClient())
            {
                string responseBody = await httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(responseBody);
                var text = htmlDocument.DocumentNode.InnerText;
                return text[..Math.Min(199999, text.Length)];
            }
        }

        public async Task<AiFunctionResult> Execute(IAiAgent api, Dictionary<string, string> parameters, string userId, CancellationToken cancellationToken = default)
        {
            if (!parameters.TryGetValue("url", out string? url))
            {
                throw new ArgumentException("The \"url\" argument is not found");
            }

            if (string.IsNullOrEmpty(url))
            {
                throw new ArgumentException("The \"url\" value IsNullOrEmpty");
            }

            if (!parameters.TryGetValue("question", out string? question))
            {
                throw new ArgumentException("The \"question\" argument is not found");
            }

            if (string.IsNullOrEmpty(question))
            {
                throw new ArgumentException("The \"question\" value IsNullOrEmpty");
            }

            string textContent =
                await GetTextContentOnly(new Uri(url), cancellationToken).ConfigureAwait(false);
            return new AiFunctionResult((await api.GetResponse(
                "I am tasked with extracting facts from the given text.", question,
                textContent, cancellationToken).ConfigureAwait(false))!);
        }
    }
}