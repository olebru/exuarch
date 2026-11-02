using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpParser
    {
        public static readonly HashSet<string> BaseTypes = new HashSet<string> { "int", "short", "bool", "char", "string", "void", "var" };
        public static readonly HashSet<string> UnsupportedTypes = new HashSet<string> { "long", "uint", "ushort", "byte", "sbyte", "float", "double", "decimal", "object", "ulong" };
        private static readonly HashSet<string> Modifiers = new HashSet<string> { "static", "public", "private", "internal", "protected", "readonly", "partial", "sealed" };

        private readonly List<SharpToken> toks;
        private readonly bool runtimeNames;
        private int p;

        public SharpParser(List<SharpToken> tokens, bool runtimeNames = false)
        {
            toks = tokens;
            this.runtimeNames = runtimeNames;
            ClassNames = new HashSet<string>();
            for (int k = 0; k + 1 < tokens.Count; k++)
            {
                if (tokens[k].Kind == "kw" && tokens[k].Text == "class" && tokens[k + 1].Kind == "id") ClassNames.Add(tokens[k + 1].Text);
            }
        }

        public HashSet<string> ClassNames { get; set; }

        private SharpToken Peek(int n = 0) => toks[Math.Min(p + n, toks.Count - 1)];

        private SharpToken Next()
        {
            var t = toks[Math.Min(p, toks.Count - 1)];
            p++;
            return t;
        }

        private bool Is(string v, int n = 0)
        {
            var t = Peek(n);
            return t.IsWord && t.Text == v;
        }

        private SharpToken Accept(string v) => Is(v) ? Next() : null;

        private SharpToken Expect(string v, string what = null)
        {
            if (Is(v)) return Next();
            var t = Peek();
            throw new SharpCompileException($"expected '{v}'{(what != null ? " " + what : "")} but found {t.Describe()}", t.Line, t.Col);
        }

        private static SharpCompileException Found(string what, SharpToken t)
        {
            return new SharpCompileException($"{what} but found {t.Describe()}", t.Line, t.Col);
        }

        private SharpToken Ident(string what = "a name")
        {
            var t = Peek();
            if (t.Kind != "id") throw Found($"expected {what}", t);
            Next();
            if (t.Text.StartsWith("__") && !runtimeNames) throw new SharpCompileException("names starting with __ are kept for the compiler's own labels", t.Line, t.Col);
            return t;
        }

        private static bool IsTypeKeyword(SharpToken t) => t.Kind == "kw" && (BaseTypes.Contains(t.Text) || UnsupportedTypes.Contains(t.Text));

        private bool IsTypeStart(int n = 0) => IsTypeKeyword(Peek(n)) || IsClassName(n);

        private bool IsClassName(int n = 0)
        {
            var t = Peek(n);
            return t.Kind == "id" && ClassNames.Contains(t.Text);
        }

        private SharpTypeRef Type()
        {
            var t = Next();
            if (UnsupportedTypes.Contains(t.Text)) throw new SharpCompileException($"'{t.Text}' is not supported: SHARP-16 has 16-bit int, bool, char and string", t.Line, t.Col);
            var name = t.Text == "short" ? "int" : t.Text;
            while (Is("[") && Is("]", 1))
            {
                Next();
                Next();
                name += "[]";
            }
            return new SharpTypeRef { Name = name, Line = t.Line, Col = t.Col };
        }

        private HashSet<string> SkipModifiers()
        {
            var mods = new HashSet<string>();
            while (Peek().Kind == "kw" && Modifiers.Contains(Peek().Text)) mods.Add(Next().Text);
            return mods;
        }

        private bool AtEof => Peek().Kind == "eof";

        public SharpUnit Program()
        {
            var unit = new SharpUnit();
            while (!AtEof) TopLevel(unit, false);
            return unit;
        }

        private void TopLevel(SharpUnit unit, bool inNamespace)
        {
            if (Is("using")) { SkipUsing(); return; }
            if (Is("namespace")) { Namespace(unit); return; }
            int save = p;
            SkipModifiers();
            if (Is("class") || Is("struct")) { Class(unit); return; }
            if (inNamespace) throw Found("expected a class", Peek());
            if (IsTypeStart() && LooksLikeMethod()) { Method(unit, null); return; }
            p = save;
            unit.Statements.Add(Statement());
        }

        private void SkipUsing()
        {
            while (!Is(";") && !AtEof) Next();
            Expect(";");
        }

        private void Namespace(SharpUnit unit)
        {
            Next();
            Ident("a namespace name");
            while (Accept(".") != null) Ident();
            if (Accept(";") != null) return;
            Expect("{");
            while (!Is("}"))
            {
                if (AtEof) Expect("}");
                TopLevel(unit, true);
            }
            Expect("}");
        }

        private void Class(SharpUnit unit)
        {
            var kw = Next();
            if (kw.Text == "struct") throw new SharpCompileException("structs are not supported", kw.Line, kw.Col);
            var name = Ident("a class name");
            if (unit.Classes.Any(c => c.Name == name.Text)) throw new SharpCompileException($"class '{name.Text}' is defined more than once", name.Line, name.Col);
            var cls = new SharpClass { Name = name.Text, Line = name.Line, Col = name.Col };
            unit.Classes.Add(cls);
            Expect("{");
            while (!Is("}"))
            {
                if (AtEof) Expect("}", "to close the class");
                Member(unit, cls);
            }
            Expect("}");
        }

        private bool LooksLikeMethod()
        {
            int n = 1;
            while (Is("[", n) && Is("]", n + 1)) n += 2;
            return Peek(n).Kind == "id" && Is("(", n + 1);
        }

        private void Member(SharpUnit unit, SharpClass cls)
        {
            var mods = SkipModifiers();
            if (Is("class")) { var t = Peek(); throw new SharpCompileException("nested classes are not supported", t.Line, t.Col); }
            bool isConst = Accept("const") != null;
            if (!isConst && Peek().Kind == "id" && Peek().Text == cls.Name && Is("(", 1)) { Constructor(cls, mods); return; }
            if (!IsTypeStart()) throw Found("expected a field or a method", Peek());
            bool isStatic = isConst || mods.Contains("static");
            if (!isConst && LooksLikeMethod()) { MemberMethod(unit, cls, isStatic); return; }
            Fields(unit, cls, isConst, isStatic);
        }

        private void Constructor(SharpClass cls, HashSet<string> mods)
        {
            var t = Peek();
            if (mods.Contains("static")) throw new SharpCompileException("static constructors are not supported", t.Line, t.Col);
            if (cls.Ctor != null) throw new SharpCompileException($"{cls.Name} has more than one constructor (overloads are not supported)", t.Line, t.Col);
            cls.Ctor = Method(null, new SharpTypeRef { Name = "void", Line = t.Line, Col = t.Col });
        }

        private void MemberMethod(SharpUnit unit, SharpClass cls, bool isStatic)
        {
            var m = Method(isStatic ? unit : null, null);
            m.Owner = cls.Name;
            if (!isStatic) cls.Methods.Add(m);
        }

        private void Fields(SharpUnit unit, SharpClass cls, bool isConst, bool isStatic)
        {
            var type = Type();
            do
            {
                var name = Ident("a field name");
                SharpNode init = null;
                if (Accept("=") != null) init = Is("{") ? ArrayInit() : Expr();
                var field = new SharpField { Type = type, Name = name.Text, Init = init, IsConst = isConst, Owner = cls.Name, Line = name.Line, Col = name.Col };
                (isStatic ? unit.Fields : cls.Fields).Add(field);
            } while (Accept(",") != null);
            Expect(";");
        }

        private SharpMethod Method(SharpUnit unit, SharpTypeRef ctorType)
        {
            var ret = ctorType ?? Type();
            var name = Ident("a method name");
            Expect("(");
            var parameters = Parameters();
            Expect(")");
            var body = Accept("=>") != null ? ExpressionBody(ret) : Block();
            var m = new SharpMethod { Name = name.Text, Ret = ret, Params = parameters, Body = body, Line = name.Line, Col = name.Col };
            unit?.Methods.Add(m);
            return m;
        }

        private List<SharpParam> Parameters()
        {
            var parameters = new List<SharpParam>();
            if (Is(")")) return parameters;
            do parameters.Add(Parameter());
            while (Accept(",") != null);
            return parameters;
        }

        private SharpParam Parameter()
        {
            if (Is("ref") || Is("out")) { var t = Peek(); throw new SharpCompileException("ref and out parameters are not supported", t.Line, t.Col); }
            if (!IsTypeStart()) throw Found("expected a parameter type", Peek());
            var type = Type();
            var pn = Ident("a parameter name");
            return new SharpParam { Type = type, Name = pn.Text, Line = pn.Line, Col = pn.Col };
        }

        private SharpNode ExpressionBody(SharpTypeRef ret)
        {
            var e = Expr();
            Expect(";");
            var statement = new SharpNode { K = ret.Name == "void" ? "expr" : "return", E = e, Line = e.Line, Col = e.Col };
            return new SharpNode { K = "block", Statements = new List<SharpNode> { statement }, Line = e.Line };
        }
    }
}
