using System;
using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private static readonly Dictionary<string, int> Keys = new Dictionary<string, int> { ["Up"] = 1, ["Down"] = 2, ["Left"] = 4, ["Right"] = 8, ["Space"] = 16 };

        private static readonly Dictionary<string, Func<SharpGenerator, SharpNode, int?>> ConstKinds = new Dictionary<string, Func<SharpGenerator, SharpNode, int?>>
        {
            ["num"] = (g, e) => S16(e.Value),
            ["char"] = (g, e) => S16(e.Value),
            ["bool"] = (g, e) => S16(e.Value),
            ["name"] = (g, e) => g.ConstName(e),
            ["member"] = (g, e) => g.ConstMember(e),
            ["cast"] = (g, e) => g.ConstCast(e),
            ["unary"] = (g, e) => g.ConstUnary(e),
            ["bin"] = (g, e) => g.ConstBinary(e),
            ["cond"] = (g, e) => g.ConstConditional(e),
        };

        private static readonly Dictionary<string, Func<int, int, int>> ConstOperators = new Dictionary<string, Func<int, int, int>>
        {
            ["+"] = (a, b) => S16(a + b),
            ["-"] = (a, b) => S16(a - b),
            ["*"] = (a, b) => Clamp(a * b),
            ["&"] = (a, b) => S16(a & b),
            ["|"] = (a, b) => S16(a | b),
            ["^"] = (a, b) => S16(a ^ b),
            ["<<"] = (a, b) => S16(a << (b & 15)),
            [">>"] = (a, b) => S16(a >> (b & 15)),
            [">>>"] = (a, b) => S16((int)((uint)(a & 0xffff) >> (b & 15))),
            ["=="] = (a, b) => Bool(a == b),
            ["!="] = (a, b) => Bool(a != b),
            ["<"] = (a, b) => Bool(a < b),
            [">"] = (a, b) => Bool(a > b),
            ["<="] = (a, b) => Bool(a <= b),
            [">="] = (a, b) => Bool(a >= b),
            ["&&"] = (a, b) => Bool(a != 0 && b != 0),
            ["||"] = (a, b) => Bool(a != 0 || b != 0),
        };

        private static int S16(int x) => ((x & 0xffff) ^ 0x8000) - 0x8000;

        private static int Clamp(int x) => Math.Max(-32768, Math.Min(32767, x));

        private static int Bool(bool b) => b ? 1 : 0;

        private int? ConstValue(SharpNode e)
        {
            if (e == null) return null;
            return ConstKinds.TryGetValue(e.K, out var f) ? f(this, e) : null;
        }

        private int? ConstName(SharpNode e)
        {
            var scoped = ScopedVariable(e.Name);
            if (scoped != null) return scoped.Kind == "const" ? scoped.Value : null;
            var g = globals.Get(e.Name);
            return g != null && g.Kind == "const" ? g.Value : null;
        }

        private int? ConstMember(SharpNode e)
        {
            if (e.Obj.K != "name") return null;
            var obj = e.Obj.Name;
            if (obj == "Keys" && Keys.ContainsKey(e.Name) && !IsVariable("Keys")) return Keys[e.Name];
            if (obj == "int" || obj == "short") return IntLimit(e.Name);
            return IsClassName(obj) ? ConstOfClass(obj, e.Name) : null;
        }

        private static int? IntLimit(string name)
        {
            return name switch
            {
                "MaxValue" => 32767,
                "MinValue" => -32768,
                _ => null,
            };
        }

        private int? ConstOfClass(string cls, string name)
        {
            var g = globals.Get(name);
            return g != null && g.Kind == "const" && g.Field != null && g.Field.Owner == cls ? g.Value : null;
        }

        private int? ConstCast(SharpNode e) => e.Type.Name == "int" || e.Type.Name == "char" ? ConstValue(e.E) : null;

        private int? ConstUnary(SharpNode e)
        {
            var v = ConstValue(e.E);
            if (v == null) return null;
            return e.Op switch
            {
                "-" => S16(-v.Value),
                "+" => v,
                "~" => S16(~v.Value),
                "!" => v.Value != 0 ? 0 : 1,
                _ => null,
            };
        }

        private int? ConstBinary(SharpNode e)
        {
            var a = ConstValue(e.L);
            var b = ConstValue(e.R);
            if (a == null || b == null) return null;
            if (e.Op == "/" || e.Op == "%") return ConstDivision(e, a.Value, b.Value);
            return ConstOperators.TryGetValue(e.Op, out var op) ? op(a.Value, b.Value) : null;
        }

        private static int ConstDivision(SharpNode e, int a, int b)
        {
            if (b == 0) throw Fail("division by the constant 0", e.R);
            return e.Op == "/" ? Clamp(a / b) : S16(a % b);
        }

        private int? ConstConditional(SharpNode e)
        {
            var c = ConstValue(e.C);
            if (c == null) return null;
            return ConstValue(c.Value != 0 ? e.A : e.B);
        }
    }
}
