using System;
using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpParser
    {
        private static readonly HashSet<string> StatementExpressions = new HashSet<string> { "assign", "call", "pre", "post", "newobj" };

        private static readonly Dictionary<string, Func<SharpParser, SharpToken, SharpNode>> KeywordStatements = new Dictionary<string, Func<SharpParser, SharpToken, SharpNode>>
        {
            ["if"] = (parser, t) => parser.If(t),
            ["while"] = (parser, t) => parser.While(t),
            ["do"] = (parser, t) => parser.Do(t),
            ["for"] = (parser, t) => parser.For(t),
            ["foreach"] = (parser, t) => parser.Foreach(t),
            ["switch"] = (parser, t) => parser.SwitchStatement(),
            ["break"] = (parser, t) => parser.Jump("break", t),
            ["continue"] = (parser, t) => parser.Jump("continue", t),
            ["return"] = (parser, t) => parser.Return(t),
            ["else"] = (parser, t) => throw new SharpCompileException("'else' without an 'if'", t.Line, t.Col),
        };

        private SharpNode Block()
        {
            var open = Expect("{");
            var body = new List<SharpNode>();
            while (!Is("}"))
            {
                if (AtEof) Expect("}", "to close the block");
                body.Add(Statement());
            }
            var close = Expect("}");
            return new SharpNode { K = "block", Statements = body, Line = open.Line, Col = open.Col, EndLine = close.Line };
        }

        private SharpNode Statement()
        {
            var t = Peek();
            if (Is("{")) return Block();
            if (Accept(";") != null) return new SharpNode { K = "empty", Line = t.Line, Col = t.Col };
            if (Is("const") || (IsTypeStart() && !Is("(", 1) && !Is(".", 1)))
            {
                var d = Declaration();
                Expect(";");
                return d;
            }
            if (t.Kind == "kw" && KeywordStatements.TryGetValue(t.Text, out var keyword)) return keyword(this, t);
            return ExpressionStatement(t);
        }

        private SharpNode ExpressionStatement(SharpToken t)
        {
            if (IsTypeStart() && LooksLikeMethod()) throw new SharpCompileException("local functions are only supported at the top level of a file", t.Line, t.Col);
            var e = Expr();
            if (!StatementExpressions.Contains(e.K)) throw new SharpCompileException("only assignment, call, increment, decrement and new expressions can be used as a statement", e.Line, e.Col);
            Expect(";", "after the statement");
            return new SharpNode { K = "expr", E = e, Line = t.Line, Col = t.Col };
        }

        private SharpNode Condition()
        {
            Expect("(");
            var c = Expr();
            Expect(")");
            return c;
        }

        private SharpNode If(SharpToken t)
        {
            Next();
            var c = Condition();
            var body = Statement();
            var els = Accept("else") != null ? Statement() : null;
            return new SharpNode { K = "if", C = c, Body = body, Else = els, Line = t.Line, Col = t.Col };
        }

        private SharpNode While(SharpToken t)
        {
            Next();
            var c = Condition();
            return new SharpNode { K = "while", C = c, Body = Statement(), Line = t.Line, Col = t.Col };
        }

        private SharpNode Do(SharpToken t)
        {
            Next();
            var body = Statement();
            Expect("while");
            var c = Condition();
            Expect(";");
            return new SharpNode { K = "do", Body = body, C = c, Line = t.Line, Col = t.Col };
        }

        private SharpNode For(SharpToken t)
        {
            Next();
            Expect("(");
            var init = Is(";") ? null : ForInit(t);
            Expect(";");
            var c = Is(";") ? null : Expr();
            Expect(";");
            var update = new List<SharpNode>();
            if (!Is(")")) update = ExpressionList();
            Expect(")");
            return new SharpNode { K = "for", Init = init, C = c, Update = update, Body = Statement(), Line = t.Line, Col = t.Col };
        }

        private SharpNode ForInit(SharpToken t)
        {
            if (IsTypeStart()) return Declaration();
            return new SharpNode { K = "exprs", Exprs = ExpressionList(), Line = t.Line, Col = t.Col };
        }

        private List<SharpNode> ExpressionList()
        {
            var es = new List<SharpNode> { Expr() };
            while (Accept(",") != null) es.Add(Expr());
            return es;
        }

        private SharpNode Foreach(SharpToken t)
        {
            Next();
            Expect("(");
            if (!IsTypeStart()) Expect("var");
            var type = Type();
            var name = Ident("a loop variable");
            Expect("in");
            var coll = Expr();
            Expect(")");
            return new SharpNode { K = "foreach", Type = type, Name = name.Text, Collection = coll, Body = Statement(), Line = t.Line, Col = t.Col };
        }

        private SharpNode Jump(string kind, SharpToken t)
        {
            Next();
            Expect(";");
            return new SharpNode { K = kind, Line = t.Line, Col = t.Col };
        }

        private SharpNode Return(SharpToken t)
        {
            Next();
            var e = Is(";") ? null : Expr();
            Expect(";");
            return new SharpNode { K = "return", E = e, Line = t.Line, Col = t.Col };
        }

        private SharpNode Declaration()
        {
            var t = Peek();
            bool isConst = Accept("const") != null;
            var type = Type();
            if (type.Name == "void") throw new SharpCompileException("a variable can not be void", type.Line, type.Col);
            var decls = new List<SharpDecl>();
            do decls.Add(Declarator(type, isConst));
            while (Accept(",") != null);
            return new SharpNode { K = "var", Type = type, Decls = decls, IsConst = isConst, Line = t.Line, Col = t.Col };
        }

        private SharpDecl Declarator(SharpTypeRef type, bool isConst)
        {
            var name = Ident("a variable name");
            SharpNode init = null;
            if (Accept("=") != null) init = Is("{") ? ArrayInit() : Expr();
            else if (type.Name == "var") throw new SharpCompileException("'var' needs an initial value", name.Line, name.Col);
            else if (isConst) throw new SharpCompileException("a const needs a value", name.Line, name.Col);
            return new SharpDecl { Name = name.Text, Init = init, Line = name.Line, Col = name.Col };
        }

        private SharpNode ArrayInit()
        {
            var open = Expect("{");
            var items = new List<SharpNode>();
            if (!Is("}"))
            {
                do
                {
                    if (Is("}")) break;
                    items.Add(Expr());
                } while (Accept(",") != null);
            }
            Expect("}");
            return new SharpNode { K = "arrinit", Items = items, Line = open.Line, Col = open.Col };
        }

        private SharpNode SwitchStatement()
        {
            var t = Next();
            var e = Condition();
            Expect("{");
            var sections = new List<SharpSwitchSection>();
            while (!Is("}")) sections.Add(SwitchSection());
            Expect("}");
            return new SharpNode { K = "switch", E = e, Sections = sections, Line = t.Line, Col = t.Col };
        }

        private SharpSwitchSection SwitchSection()
        {
            var labels = new List<SharpCaseLabel>();
            while (Is("case") || Is("default")) labels.Add(CaseLabel());
            if (labels.Count == 0) throw Found("expected 'case' or 'default'", Peek());
            var body = new List<SharpNode>();
            while (!Is("case") && !Is("default") && !Is("}"))
            {
                if (AtEof) Expect("}");
                body.Add(Statement());
            }
            return new SharpSwitchSection { Labels = labels, Body = body };
        }

        private SharpCaseLabel CaseLabel()
        {
            var lt = Next();
            var label = lt.Text == "default"
                ? new SharpCaseLabel { IsDefault = true, Line = lt.Line, Col = lt.Col }
                : new SharpCaseLabel { E = Expr(), Line = lt.Line, Col = lt.Col };
            Expect(":");
            return label;
        }
    }
}
