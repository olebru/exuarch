using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpParser
    {
        private static readonly HashSet<string> AssignOps = new HashSet<string> { "=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", ">>>=" };
        private static readonly HashSet<string> Assignable = new HashSet<string> { "name", "index", "member" };
        private static readonly HashSet<string> UnaryOps = new HashSet<string> { "-", "+", "!", "~" };

        private static readonly string[][] BinaryLevels =
        {
            new[] { "||" }, new[] { "&&" }, new[] { "|" }, new[] { "^" }, new[] { "&" }, new[] { "==", "!=" }, new[] { "<", ">", "<=", ">=" },
            new[] { "<<", ">>", ">>>" }, new[] { "+", "-" }, new[] { "*", "/", "%" },
        };

        private static readonly Dictionary<string, Func<SharpParser, SharpToken, SharpNode>> Primaries = new Dictionary<string, Func<SharpParser, SharpToken, SharpNode>>
        {
            ["num"] = (parser, t) => new SharpNode { K = "num", Value = t.Number, Line = t.Line, Col = t.Col },
            ["char"] = (parser, t) => new SharpNode { K = "char", Value = t.Number, Line = t.Line, Col = t.Col },
            ["str"] = (parser, t) => new SharpNode { K = "str", Text = t.Text, Line = t.Line, Col = t.Col },
            ["interp"] = (parser, t) => parser.Interpolation(t),
            ["id"] = (parser, t) => new SharpNode { K = "name", Name = t.Text, Line = t.Line, Col = t.Col },
            ["kw"] = (parser, t) => parser.KeywordPrimary(t),
            ["op"] = (parser, t) => parser.Parenthesised(t),
        };

        public SharpNode Expr() => Assignment();

        private SharpNode Assignment()
        {
            var left = Conditional();
            var t = Peek();
            if (t.Kind != "op" || !AssignOps.Contains(t.Text)) return left;
            Next();
            if (!Assignable.Contains(left.K)) throw new SharpCompileException("the left side of an assignment must be a variable or an array element", left.Line, left.Col);
            var right = Is("{") ? ArrayInit() : Assignment();
            return new SharpNode { K = "assign", Op = t.Text, Target = left, E = right, Line = t.Line, Col = t.Col };
        }

        private SharpNode Conditional()
        {
            var c = Binary(0);
            if (!Is("?")) return c;
            var q = Next();
            var a = Assignment();
            Expect(":");
            var b = Assignment();
            return new SharpNode { K = "cond", C = c, A = a, B = b, Line = q.Line, Col = q.Col };
        }

        private SharpNode Binary(int level)
        {
            if (level >= BinaryLevels.Length) return Unary();
            var left = Binary(level + 1);
            for (;;)
            {
                var t = Peek();
                if (t.Kind != "op" || !BinaryLevels[level].Contains(t.Text)) return left;
                Next();
                var right = Binary(level + 1);
                left = new SharpNode { K = "bin", Op = t.Text, L = left, R = right, Line = t.Line, Col = t.Col };
            }
        }

        private SharpNode Unary()
        {
            var t = Peek();
            if (t.Kind == "op" && UnaryOps.Contains(t.Text)) { Next(); return new SharpNode { K = "unary", Op = t.Text, E = Unary(), Line = t.Line, Col = t.Col }; }
            if (t.Kind == "op" && (t.Text == "++" || t.Text == "--")) { Next(); return new SharpNode { K = "pre", Op = t.Text, E = Unary(), Line = t.Line, Col = t.Col }; }
            if (Is("(") && IsTypeKeyword(Peek(1)) && Cast(t) is { } cast) return cast;
            return Postfix();
        }

        private SharpNode Cast(SharpToken t)
        {
            int save = p;
            Next();
            var type = Type();
            if (Accept(")") != null) return new SharpNode { K = "cast", Type = type, E = Unary(), Line = t.Line, Col = t.Col };
            p = save;
            return null;
        }

        private SharpNode Postfix()
        {
            var e = Primary();
            for (;;)
            {
                var next = PostfixStep(e);
                if (next == null) return e;
                e = next;
            }
        }

        private SharpNode PostfixStep(SharpNode e)
        {
            var t = Peek();
            if (Accept("(") != null) return new SharpNode { K = "call", Callee = e, Args = Arguments(), Line = e.Line, Col = e.Col };
            if (Accept("[") != null)
            {
                var idx = Expr();
                Expect("]");
                return new SharpNode { K = "index", Obj = e, Index = idx, Line = t.Line, Col = t.Col };
            }
            if (Accept(".") != null) return MemberAccess(e);
            if (t.Kind == "op" && (t.Text == "++" || t.Text == "--")) { Next(); return new SharpNode { K = "post", Op = t.Text, E = e, Line = t.Line, Col = t.Col }; }
            return null;
        }

        private List<SharpNode> Arguments()
        {
            var args = new List<SharpNode>();
            if (!Is(")"))
            {
                do args.Add(Expr());
                while (Accept(",") != null);
            }
            Expect(")", "to close the argument list");
            return args;
        }

        private SharpNode MemberAccess(SharpNode e)
        {
            var name = Peek();
            if (name.Kind != "id" && name.Kind != "kw") throw Found("expected a member name", name);
            Next();
            return new SharpNode { K = "member", Obj = e, Name = name.Text, Line = name.Line, Col = name.Col };
        }

        private SharpNode Primary()
        {
            var t = Next();
            var node = Primaries.TryGetValue(t.Kind, out var make) ? make(this, t) : null;
            if (node == null) throw Found("expected an expression", t);
            return node;
        }

        private SharpNode Interpolation(SharpToken t)
        {
            var parts = t.Pieces.Select(piece => piece.Text != null ? (object)piece.Text : ParseExpression(piece.Source, piece.Line, piece.Col, ClassNames)).ToList();
            return new SharpNode { K = "interp", Parts = parts, Line = t.Line, Col = t.Col };
        }

        private SharpNode Parenthesised(SharpToken t)
        {
            if (t.Text != "(") return null;
            var e = Expr();
            Expect(")");
            return e;
        }

        private SharpNode KeywordPrimary(SharpToken t)
        {
            if (t.Text == "true" || t.Text == "false") return new SharpNode { K = "bool", Value = t.Text == "true" ? 1 : 0, Line = t.Line, Col = t.Col };
            if (t.Text == "null") return new SharpNode { K = "null", Line = t.Line, Col = t.Col };
            if (t.Text == "this") return new SharpNode { K = "this", Line = t.Line, Col = t.Col };
            if (t.Text == "new") return New(t);
            if (BaseTypes.Contains(t.Text) && Is(".")) return new SharpNode { K = "name", Name = t.Text, Line = t.Line, Col = t.Col };
            return null;
        }

        private SharpNode New(SharpToken t)
        {
            if (!IsTypeStart()) throw Found("expected a type after new", Peek());
            var type = Type();
            if (ClassNames.Contains(type.Name) && Accept("(") != null) return new SharpNode { K = "newobj", Cls = type.Name, Args = Arguments(), Line = t.Line, Col = t.Col };
            if (type.Name == "string" && Accept("(") != null) return new SharpNode { K = "newstr", Args = Arguments(), Line = t.Line, Col = t.Col };
            if (Accept("[") != null) return NewArray(type, t);
            if (type.Name.EndsWith("[]") && Is("{")) return new SharpNode { K = "newarr", Elem = type.Name.Substring(0, type.Name.Length - 2), Init = ArrayInit(), Line = t.Line, Col = t.Col };
            var message = ClassNames.Contains(type.Name) ? $"expected '(' after new {type.Name}" : "only arrays, strings and objects of your own classes can be created with new";
            throw new SharpCompileException(message, t.Line, t.Col);
        }

        private SharpNode NewArray(SharpTypeRef type, SharpToken t)
        {
            if (Accept("]") != null) return new SharpNode { K = "newarr", Elem = type.Name, Init = ArrayInit(), Line = t.Line, Col = t.Col };
            var size = Expr();
            Expect("]");
            var init = Is("{") ? ArrayInit() : null;
            return new SharpNode { K = "newarr", Elem = type.Name, Size = size, Init = init, Line = t.Line, Col = t.Col };
        }

        public static SharpNode ParseExpression(string src, int line, int col, HashSet<string> classNames)
        {
            var parser = new SharpParser(SharpLexer.Lex(src, line, col)) { ClassNames = classNames };
            var e = parser.Expr();
            if (parser.Peek().Kind != "eof")
            {
                var t = parser.Peek();
                throw new SharpCompileException($"unexpected {t.Describe()} in {{…}}", t.Line, t.Col);
            }
            return e;
        }
    }
}
