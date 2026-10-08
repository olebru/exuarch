using Exuarch.Web.Components;

namespace Exuarch.Web.Tests;

public class ReadmeMarkdownTests
{
    [Fact]
    public void WebLinksOpenInANewTabAndAppLinksDoNot()
    {
        var html = ReadmeMarkdown.ToHtml("[videos](https://www.youtube.com/playlist?list=PLOZxNQ9ktMYM) and [fetch](exuarch:guide/fetch-routine) and https://example.org");
        Assert.Contains("href=\"https://www.youtube.com/playlist?list=PLOZxNQ9ktMYM\" target=\"_blank\" rel=\"noopener\"", html);
        Assert.Contains("href=\"https://example.org\" target=\"_blank\" rel=\"noopener\"", html);
        Assert.Contains("href=\"exuarch:guide/fetch-routine\">", html);
        Assert.DoesNotContain("exuarch:guide/fetch-routine\" target", html);
    }

    [Fact]
    public void OtherSchemesAreDisarmed()
    {
        var html = ReadmeMarkdown.ToHtml("[x](javascript:alert(1))");
        Assert.Contains("href=\"#\"", html);
        Assert.DoesNotContain("javascript:", html);
    }
}
