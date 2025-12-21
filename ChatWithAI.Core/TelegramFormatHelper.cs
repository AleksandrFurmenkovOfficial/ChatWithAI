using Markdig;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatWithAI.Core
{
    public static class TelegramFormatHelper
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UsePipeTables()
            .DisableHtml()
            .Build();

        private static readonly Regex MultipleNewLinesRegex = new(@"\n{3,}", RegexOptions.Compiled);

        public static string ConvertToTelegramHtml(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "";

            var html = Markdown.ToHtml(markdown, Pipeline);
            var sb = new StringBuilder(html);

            sb.Replace("<p>", "");
            sb.Replace("</p>", "\n");
            sb.Replace("<br>", "\n");
            sb.Replace("<br />", "\n");

            sb.Replace("<ul>", "");
            sb.Replace("</ul>", "");
            sb.Replace("<ol>", "");
            sb.Replace("</ol>", "");

            sb.Replace("<li>", "• ");
            sb.Replace("</li>", "\n");

            for (int i = 1; i <= 6; i++)
            {
                sb.Replace($"<h{i}>", "<b>");
                sb.Replace($"</h{i}>", "</b>\n");
            }

            sb.Replace("<strong>", "<b>");
            sb.Replace("</strong>", "</b>");
            sb.Replace("<em>", "<i>");
            sb.Replace("</em>", "</i>");
            sb.Replace("<del>", "<s>");
            sb.Replace("</del>", "</s>");

            var result = sb.ToString().Trim();

            return MultipleNewLinesRegex.Replace(result, "\n\n");
        }
    }
}