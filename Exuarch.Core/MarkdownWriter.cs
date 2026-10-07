using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace Exuarch.Core
{
    // Writes a markdown page block by block, with a blank line between blocks.
    internal sealed class MarkdownWriter
    {
        private readonly StringBuilder text = new StringBuilder();

        public MarkdownWriter Title(string title)
        {
            return Block($"# {title}");
        }

        public MarkdownWriter Paragraph(string paragraph)
        {
            return Block(paragraph);
        }

        // A "## " heading, and a paragraph that introduces the section when there is one.
        public MarkdownWriter Section(string heading, string introduction = null)
        {
            Block($"## {heading}");
            return introduction == null ? this : Block(introduction);
        }

        public MarkdownWriter Table(string[] headers, IEnumerable<string[]> rows)
        {
            var separator = $"|{string.Join("|", headers.Select(_ => "---"))}|";
            return Block(string.Join("\n", new[] { Row(headers), separator }.Concat(rows.Select(Row))));
        }

        public MarkdownWriter BulletList(IEnumerable<string> items)
        {
            return Block(string.Join("\n", items.Select(item => $"- {item}")));
        }

        // A table cell: on one line, with its pipes escaped.
        public static string Cell(string text)
        {
            return (text ?? "").Replace("|", "\\|").Replace("\n", " ");
        }

        public override string ToString()
        {
            return text.ToString().TrimEnd('\n');
        }

        private static string Row(string[] cells)
        {
            return $"| {string.Join(" | ", cells)} |";
        }

        private MarkdownWriter Block(string block)
        {
            text.Append(block).Append("\n\n");
            return this;
        }
    }
}
