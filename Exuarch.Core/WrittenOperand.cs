using System.Collections.Generic;
using System.Linq;
namespace Exuarch.Core
{
    public sealed class WrittenTerm
    {
        public SourceToken Sign { get; init; }
        public SourceToken Token { get; init; }
        public bool IsNegated { get { return Sign?.Kind == TokenKind.Minus; } }
    }

    public sealed class WrittenOperand
    {
        public SourceToken Open { get; set; }
        public SourceToken Close { get; set; }
        public SourceToken Increment { get; set; }
        public List<WrittenTerm> Terms { get; } = new List<WrittenTerm>();

        public bool IsBracketed { get { return Open != null; } }
        public bool IsPostIncrement { get { return Increment != null; } }
        public bool IsPlain { get { return !IsBracketed && Terms.Count == 1; } }

        public string Text()
        {
            if (!IsBracketed) return Terms.Count == 0 ? "" : WrittenText(Terms[0].Token);
            var inside = string.Concat(Terms.Select((term, index) => (index == 0 ? "" : term.IsNegated ? " - " : " + ") + WrittenText(term.Token)));
            return "[" + inside + "]" + (IsPostIncrement ? "+" : "");
        }

        public static string WrittenText(SourceToken token)
        {
            return token.Kind == TokenKind.Number || token.Kind == TokenKind.Character ? token.Text.TrimStart('#') : token.Text;
        }
    }
}
