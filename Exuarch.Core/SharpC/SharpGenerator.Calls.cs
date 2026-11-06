using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private const string KnownBuiltins = "Console.Write, Console.WriteLine, Console.Clear, Screen.Plot, Screen.MoveTo, Screen.Put, Screen.Clear, Screen.Rgb, Keys.Read, Math.Abs, Math.Min, Math.Max, GC.Collect and Machine.Halt";

        private static readonly Dictionary<string, Func<SharpGenerator, SharpNode, string>> Builtins = new Dictionary<string, Func<SharpGenerator, SharpNode, string>>
        {
            ["Console.Write"] = (g, e) => g.GenWrite(e, false),
            ["Console.WriteLine"] = (g, e) => g.GenWrite(e, true),
            ["Console.Clear"] = (g, e) => g.GenSimpleBuiltin(e, "Console.Clear", "CLT"),
            ["Screen.Clear"] = (g, e) => g.GenSimpleBuiltin(e, "Screen.Clear", "CLS"),
            ["Screen.Plot"] = (g, e) => g.GenPlot(e),
            ["Screen.MoveTo"] = (g, e) => g.GenMoveTo(e),
            ["Screen.Put"] = (g, e) => g.GenPut(e),
            ["Screen.Rgb"] = (g, e) => g.GenRgb(e),
            ["Keys.Read"] = (g, e) => g.GenKeysRead(e),
            ["Machine.Halt"] = (g, e) => g.GenHalt(),
            ["Environment.Exit"] = (g, e) => g.GenHalt(),
            ["GC.Collect"] = (g, e) => g.GenCollect(e),
            ["Math.Abs"] = (g, e) => g.GenAbs(e),
            ["Math.Min"] = (g, e) => g.GenMinMax(e, "Math.Min"),
            ["Math.Max"] = (g, e) => g.GenMinMax(e, "Math.Max"),
        };

        private static readonly Dictionary<string, Action<SharpGenerator, SharpNode>> IntrinsicCode = new Dictionary<string, Action<SharpGenerator, SharpNode>>
        {
            ["__peek"] = (g, e) => g.GenPeek(e),
            ["__poke"] = (g, e) => g.GenPoke(e),
            ["__fp"] = (g, e) => g.GenAddressOf(e, "__fp", "LEAL", "0"),
            ["__heapStart"] = (g, e) => g.GenAddressOf(e, "__heapStart", "LEA", "__heap_start"),
            ["__roots"] = (g, e) => g.GenAddressOf(e, "__roots", "LEA", "__roots"),
        };

        private string GenCall(SharpNode e)
        {
            var c = e.Callee;
            if (c.K == "name") return GenNamedCall(e, c);
            if (c.K == "member") return GenMemberCall(e, c);
            throw Fail("this call is not supported", e);
        }

        private string GenNamedCall(SharpNode e, SharpNode c)
        {
            if (IntrinsicCode.TryGetValue(c.Name, out var intrinsic))
            {
                intrinsic(this, e);
                return Intrinsics[c.Name];
            }
            var own = fn?.Cls != null ? classes.Get(fn.Cls) : null;
            if (own != null && own.Methods.Has(c.Name)) return GenOwnMethodCall(e, c, own.Methods.Get(c.Name));
            var f = StaticFunction(c.Name);
            if (f == null) throw MissingMethod(c);
            GenArgs(f, e.Args, e, c.Name);
            Emit("CALL", f.Label ?? f.Name);
            return f.Ret.Name;
        }

        private SharpCompileException MissingMethod(SharpNode c)
        {
            var owner = fn?.Owner != null ? classes.Get(fn.Owner) : null;
            if (owner != null && owner.Methods.Has(c.Name)) return Fail($"'{c.Name}' works on a {owner.Name} object, and a static method has no object to call it on", c);
            return Fail(IsVariable(c.Name) ? $"'{c.Name}' is a variable, not a method" : $"there is no method '{c.Name}'", c);
        }

        private string GenOwnMethodCall(SharpNode e, SharpNode c, SharpMethod m)
        {
            Emit("LDL", Number(Lookup("this", c).Offset));
            Emit("PUSH");
            GenArgs(m, e.Args, e, c.Name);
            Emit("CALL", m.Label);
            return m.Ret.Name;
        }

        private string GenMemberCall(SharpNode e, SharpNode c)
        {
            var target = MemberTarget(c.Obj);
            if (target?.Static != null) return GenStaticCall(e, c, target.Static);
            if (target?.Instance != null) return GenInstanceCall(e, c, target.Instance);
            if (c.Obj.K == "name") return GenBuiltin($"{c.Obj.Name}.{c.Name}", e);
            throw Fail("this call is not supported", e);
        }

        private string GenStaticCall(SharpNode e, SharpNode c, SharpClassInfo info)
        {
            var f = StaticMember(info, c.Name, c);
            if (f.Method == null) throw Fail($"'{c.Name}' is a field, not a method", c);
            GenArgs(f.Method, e.Args, e, $"{info.Name}.{c.Name}");
            Emit("CALL", f.Method.Label ?? f.Method.Name);
            return f.Method.Ret.Name;
        }

        private string GenInstanceCall(SharpNode e, SharpNode c, SharpClassInfo info)
        {
            var m = InstanceMember(info, c.Name, c);
            if (m.Method == null) throw Fail($"'{c.Name}' is a field, not a method", c);
            GenExpr(c.Obj);
            Emit("PUSH");
            GenArgs(m.Method, e.Args, e, c.Name);
            Emit("CALL", m.Method.Label);
            return m.Method.Ret.Name;
        }

        private void GenArgs(SharpMethod f, List<SharpNode> args, SharpNode e, string name)
        {
            if (args.Count != f.Params.Count) throw Fail($"{name} takes {Number(f.Params.Count)} argument{(f.Params.Count == 1 ? "" : "s")}, not {Number(args.Count)}", e);
            for (int i = 0; i < args.Count; i++)
            {
                GenValue(args[i], f.Params[i].Type.Name, args[i]);
                Emit("PUSH");
            }
        }

        private void GenPeek(SharpNode e)
        {
            Args(e, 1, "__peek");
            GenInt(e.Args[0]);
            Emit("LDX");
        }

        private void GenPoke(SharpNode e)
        {
            Args(e, 2, "__poke");
            GenOperands(e.Args[0], e.Args[1], false);
            Emit("STX");
        }

        private void GenAddressOf(SharpNode e, string name, string mnemonic, string operand)
        {
            Args(e, 0, name);
            Emit(mnemonic, operand);
        }

        private static void Args(SharpNode e, int n, string name)
        {
            if (e.Args.Count != n) throw Fail($"{name} takes {Number(n)} argument{(n == 1 ? "" : "s")}", e);
        }

        private void GenInt(SharpNode a)
        {
            var t = GenExpr(a);
            if (!IsInt(t)) throw Fail($"expected a number, not {t}", a);
        }

        private string GenBuiltin(string name, SharpNode e)
        {
            if (Builtins.TryGetValue(name, out var gen)) return gen(this, e);
            throw Fail($"'{name}' is not available. The SHARP-16 built-ins are {KnownBuiltins}", e);
        }

        private string GenWrite(SharpNode e, bool newLine)
        {
            if (e.Args.Count > 1) GenFormattedWrite(e);
            else if (e.Args.Count == 1) PrintValue(e.Args[0]);
            if (newLine)
            {
                Emit("LDI", "10");
                Emit("OUT");
            }
            return "void";
        }

        private void GenFormattedWrite(SharpNode e)
        {
            var fmt = e.Args[0];
            if (fmt.K != "str") throw Fail("with more than one argument, the first must be a format string such as \"{0} and {1}\"", fmt);
            PrintFormat(fmt, e.Args.Skip(1).ToList());
        }

        private string GenSimpleBuiltin(SharpNode e, string name, string mnemonic)
        {
            Args(e, 0, name);
            Emit(mnemonic);
            return "void";
        }

        private string GenPlot(SharpNode e)
        {
            Args(e, 3, "Screen.Plot");
            GenInt(e.Args[0]);
            Emit("PX");
            GenInt(e.Args[1]);
            Emit("PY");
            GenInt(e.Args[2]);
            Emit("PLOT");
            return "void";
        }

        private string GenMoveTo(SharpNode e)
        {
            Args(e, 2, "Screen.MoveTo");
            GenInt(e.Args[0]);
            Emit("PX");
            GenInt(e.Args[1]);
            Emit("PY");
            return "void";
        }

        private string GenPut(SharpNode e)
        {
            Args(e, 1, "Screen.Put");
            GenInt(e.Args[0]);
            Emit("PLOT");
            return "void";
        }

        private static SharpNode Bin(string op, SharpNode l, SharpNode r, SharpPos at) => new SharpNode { K = "bin", Op = op, L = l, R = r, Line = at.Line, Col = at.Col };

        private static SharpNode Num(int v) => new SharpNode { K = "num", Value = v };

        private string GenRgb(SharpNode e)
        {
            Args(e, 3, "Screen.Rgb");
            var red = Bin("<<", Bin(">>>", e.Args[0], Num(3), e), Num(11), e);
            var green = Bin("<<", Bin(">>>", e.Args[1], Num(2), e), Num(5), e);
            var blue = Bin(">>>", e.Args[2], Num(3), e);
            var rgb = Bin("|", Bin("|", red, green, e), blue, e);
            var cv = ConstValue(rgb);
            if (cv != null) Emit("LDI", "0x" + (cv.Value & 0xffff).ToString("X4"));
            else GenBinary(rgb);
            return "int";
        }

        private string GenKeysRead(SharpNode e)
        {
            Args(e, 0, "Keys.Read");
            Emit("IN");
            return "int";
        }

        private string GenHalt()
        {
            Emit("HLT");
            return "void";
        }

        private string GenCollect(SharpNode e)
        {
            Args(e, 0, "GC.Collect");
            NeedsHeap = true;
            Emit("CALL", "__Collect");
            return "void";
        }

        private string GenAbs(SharpNode e)
        {
            Args(e, 1, "Math.Abs");
            GenInt(e.Args[0]);
            var l = NewLabel("abs");
            Emit("TST");
            Emit("JGE", l);
            Emit("NEG");
            Label(l);
            return "int";
        }

        private string GenMinMax(SharpNode e, string name)
        {
            Args(e, 2, name);
            var a = e.Args[0];
            var b = e.Args[1];
            if (!IsInt(TypeOf(a)) || !IsInt(TypeOf(b))) throw Fail($"{name} needs two numbers", e);
            GenOperands(a, b, false);
            var l = NewLabel(name == "Math.Min" ? "min" : "max");
            Emit("CMP");
            Emit(name == "Math.Min" ? "JGE" : "JLE", l);
            Emit("TBA");
            Label(l);
            return "int";
        }

        private void PrintValue(SharpNode a)
        {
            if (a.K == "bin" && a.Op == "+" && TypeOf(a) == "string")
            {
                PrintValue(a.L);
                PrintValue(a.R);
                return;
            }
            if (a.K == "interp") { PrintParts(a.Parts); return; }
            if (a.K == "str") { PrintText(a.Text); return; }
            var cv = ConstValue(a);
            var t = TypeOf(a);
            if (cv != null && t != "char") { PrintText(t == "bool" ? (cv.Value != 0 ? "True" : "False") : Number(cv.Value)); return; }
            GenExpr(a);
            PrintTyped(a, t);
        }

        private void PrintParts(List<object> parts)
        {
            foreach (var p in parts)
            {
                if (p is string text) PrintText(text);
                else PrintValue((SharpNode)p);
            }
        }

        private void PrintTyped(SharpNode a, string t)
        {
            var routine = t switch
            {
                "char" => "",
                "int" => "__print_int",
                "bool" => "__print_bool",
                "string" => "__print_str",
                _ => throw Fail($"can not print a {t}", a),
            };
            if (routine.Length == 0)
            {
                Emit("OUT");
                return;
            }
            Use(routine);
            Emit("CALL", routine);
        }

        private void PrintText(string s)
        {
            if (s.Length == 0) return;
            if (s.Length <= 2)
            {
                foreach (var ch in CodePoints(s))
                {
                    Emit("LDI", AsmChar(ch[0]));
                    Emit("OUT");
                }
                return;
            }
            AsmString(s);
            Emit("LEA", StringLabel(s));
            Use("__print_str");
            Emit("CALL", "__print_str");
        }

        private void PrintFormat(SharpNode fmt, List<SharpNode> args)
        {
            var s = fmt.Text;
            var text = new StringBuilder();
            for (int i = 0; i < s.Length; i++) i = FormatStep(fmt, args, s, i, text);
            PrintText(text.ToString());
        }

        private int FormatStep(SharpNode fmt, List<SharpNode> args, string s, int i, StringBuilder text)
        {
            if (IsDoubledBrace(s, i))
            {
                text.Append(s[i]);
                return i + 1;
            }
            if (s[i] != '{')
            {
                text.Append(s[i]);
                return i;
            }
            return FormatItem(fmt, args, s, i, text);
        }

        private static bool IsDoubledBrace(string s, int i) => (s[i] == '{' || s[i] == '}') && i + 1 < s.Length && s[i + 1] == s[i];

        private int FormatItem(SharpNode fmt, List<SharpNode> args, string s, int i, StringBuilder text)
        {
            int close = s.IndexOf('}', i);
            var index = close < 0 ? "" : s.Substring(i + 1, close - i - 1);
            if (close < 0 || !Regex.IsMatch(index, "^[0-9]+$")) throw Fail("format items must look like {0}, without alignment or format strings", fmt);
            if (FormatIndex(index) >= args.Count) throw Fail($"{{{index}}} has no argument", fmt);
            PrintText(text.ToString());
            text.Clear();
            PrintValue(args[(int)FormatIndex(index)]);
            return close;
        }

        private static double FormatIndex(string index) => double.Parse(index, System.Globalization.CultureInfo.InvariantCulture);

        private static IEnumerable<string> CodePoints(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    yield return s.Substring(i, 2);
                    i++;
                }
                else yield return s[i].ToString();
            }
        }

        private static string AsmString(string s)
        {
            var parts = new List<string>();
            var run = new StringBuilder();
            foreach (var ch in CodePoints(s)) AsmStringPart(ch, parts, run);
            if (run.Length > 0 || parts.Count == 0) parts.Add($"\"{run}\"");
            return string.Join(", ", parts);
        }

        private static void AsmStringPart(string ch, List<string> parts, StringBuilder run)
        {
            int v = ch[0];
            if (v > 255) throw new SharpCompileException($"'{ch}' is not a Latin-1 character, the LCD can not show it", 0, 0);
            var escaped = AsmEscape(ch[0]);
            if (escaped != null)
            {
                run.Append(escaped);
                return;
            }
            if (run.Length > 0) parts.Add($"\"{run}\"");
            run.Clear();
            parts.Add(Number(v));
        }

        private static string AsmEscape(char c)
        {
            if (c >= 32 && c < 127 && c != '"' && c != '\\') return c.ToString();
            return c switch
            {
                '\n' => "\\n",
                '\t' => "\\t",
                '"' => "\\\"",
                '\\' => "\\\\",
                _ => null,
            };
        }
    }
}
