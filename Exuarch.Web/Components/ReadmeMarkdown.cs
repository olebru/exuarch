using System;
using System.IO;
using System.Linq;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Exuarch.Web.Components
{
    // Turns a package README into HTML that is safe to show, whoever wrote the package: raw HTML is not allowed,
    // links may only go to the web (http, https, mailto), to an anchor, or into the app (exuarch:), and images become
    // links to them, so opening a README never fetches anything from a server its author picked.
    public static class ReadmeMarkdown
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .DisableHtml()
            .Build();

        public static string ToHtml(string markdown)
        {
            var document = Markdown.Parse(markdown ?? "", Pipeline);
            foreach (var link in document.Descendants<LinkInline>().ToList())
            {
                if (!Allowed(link.Url)) link.Url = "#";
                if (IsWeb(link.Url)) OpenInNewTab(link);
                if (link.IsImage)
                {
                    link.IsImage = false;
                    if (link.FirstChild == null) link.AppendChild(new LiteralInline("image"));
                }
            }
            foreach (var link in document.Descendants<AutolinkInline>())
            {
                if (!Allowed(link.Url)) link.Url = "#";
                if (IsWeb(link.Url)) OpenInNewTab(link);
            }
            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();
            return writer.ToString();
        }

        private static void OpenInNewTab(Inline link)
        {
            var attributes = link.GetAttributes();
            attributes.AddPropertyIfNotExist("target", "_blank");
            attributes.AddPropertyIfNotExist("rel", "noopener");
        }

        private static bool IsWeb(string url)
        {
            var trimmed = url?.Trim() ?? "";
            return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Allowed(string url)
        {
            if (string.IsNullOrEmpty(url)) return true;
            var trimmed = url.Trim();
            if (trimmed.StartsWith("#")) return true;
            return new[] { "http://", "https://", "mailto:", "exuarch:" }.Any(s => trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        }
    }
}
