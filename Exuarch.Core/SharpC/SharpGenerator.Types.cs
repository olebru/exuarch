using System;
using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private static readonly Dictionary<string, string> JumpFor = new Dictionary<string, string> { ["=="] = "JEQ", ["!="] = "JNE", ["<"] = "JLT", [">="] = "JGE", [">"] = "JGT", ["<="] = "JLE" };

        private static readonly Dictionary<string, string> Intrinsics = new Dictionary<string, string> { ["__peek"] = "int", ["__poke"] = "void", ["__fp"] = "int", ["__heapStart"] = "int", ["__roots"] = "int" };

        private static readonly Dictionary<string, Func<SharpGenerator, SharpNode, string>> TypeKinds = new Dictionary<string, Func<SharpGenerator, SharpNode, string>>
        {
            ["num"] = (g, e) => "int",
            ["char"] = (g, e) => "char",
            ["bool"] = (g, e) => "bool",
            ["str"] = (g, e) => "string",
            ["interp"] = (g, e) => "string",
            ["null"] = (g, e) => "null",
            ["this"] = (g, e) => g.ThisType(e),
            ["newobj"] = (g, e) => e.Cls,
            ["name"] = (g, e) => g.Lookup(e.Name, e).Type,
            ["member"] = (g, e) => g.MemberType(e),
            ["index"] = (g, e) => g.IndexType(e),
            ["cast"] = (g, e) => e.Type.Name,
            ["unary"] = (g, e) => e.Op == "!" ? "bool" : "int",
            ["pre"] = (g, e) => g.IncDecType(e),
            ["post"] = (g, e) => g.IncDecType(e),
            ["bin"] = (g, e) => g.BinaryType(e),
            ["assign"] = (g, e) => g.TypeOf(e.Target),
            ["cond"] = (g, e) => g.ConditionalType(e),
            ["call"] = (g, e) => g.CallType(e),
            ["newarr"] = (g, e) => e.Elem + "[]",
            ["arrinit"] = (g, e) => "array",
        };

        private static readonly HashSet<string> LogicalOps = new HashSet<string> { "&&", "||" };
        private static readonly HashSet<string> BitwiseOps = new HashSet<string> { "&", "|", "^" };
        private static readonly HashSet<string> EqualityOps = new HashSet<string> { "==", "!=" };

        private static bool IsComparison(string op) => JumpFor.ContainsKey(op);

        private string TypeOf(SharpNode e) => TypeKinds.TryGetValue(e.K, out var f) ? f(this, e) : "int";

        private string MemberType(SharpNode e)
        {
            if (ConstValue(e) != null) return MemberConstType(e);
            var target = MemberTarget(e.Obj);
            if (target?.Static != null) return StaticFieldType(target.Static, e);
            if (target?.Instance != null) return InstanceFieldType(target.Instance, e);
            if (e.Name == "Length" && LengthType(e) is { } length) return length;
            throw Fail($"'{e.Name}' is not supported here", e);
        }

        private string LengthType(SharpNode e)
        {
            var t = TypeOf(e.Obj);
            return IsArray(t) || t == "string" ? "int" : null;
        }

        private string StaticFieldType(SharpClassInfo info, SharpNode e)
        {
            var g = StaticMember(info, e.Name, e);
            if (g.Kind != "global") throw Fail($"'{e.Name}' is a method: call it with {e.Name}(…)", e);
            return g.Type;
        }

        private string InstanceFieldType(SharpClassInfo info, SharpNode e)
        {
            var f = InstanceMember(info, e.Name, e);
            if (f.Kind != "field") throw Fail($"'{e.Name}' is a method: call it with {e.Name}(…)", e);
            return f.Type;
        }

        private string IndexType(SharpNode e)
        {
            var t = TypeOf(e.Obj);
            if (t == "string") return "char";
            if (IsArray(t)) return ElementOf(t);
            throw Fail($"only arrays and strings can be indexed, not {t}", e);
        }

        private string IncDecType(SharpNode e) => TypeOf(e.E) == "char" ? "char" : "int";

        private string BinaryType(SharpNode e)
        {
            if (IsComparison(e.Op) || LogicalOps.Contains(e.Op)) return "bool";
            var l = TypeOf(e.L);
            var r = TypeOf(e.R);
            if (e.Op == "+" && (l == "string" || r == "string")) return "string";
            if (BitwiseOps.Contains(e.Op) && l == "bool" && r == "bool") return "bool";
            return "int";
        }

        private string ConditionalType(SharpNode e)
        {
            var a = TypeOf(e.A);
            return a == "null" ? TypeOf(e.B) : a;
        }

        private string ThisType(SharpPos e)
        {
            if (fn?.Cls == null) throw Fail("'this' can only be used in a constructor or a method that is not static", e);
            return fn.Cls;
        }

        private string MemberConstType(SharpNode e)
        {
            if (e.Obj.K == "name" && IsClassName(e.Obj.Name)) return globals.Get(e.Name).Type;
            return "int";
        }

        private string CallType(SharpNode e)
        {
            var c = e.Callee;
            if (c.K == "name") return NamedCallType(c);
            if (c.K == "member" && MemberTarget(c.Obj) is { } target) return TargetCallType(target, c);
            if (c.K == "member" && c.Obj.K == "name") return BuiltinType($"{c.Obj.Name}.{c.Name}");
            return "void";
        }

        private string TargetCallType(SharpTarget target, SharpNode c)
        {
            var f = target.Static != null ? StaticMember(target.Static, c.Name, c) : InstanceMember(target.Instance, c.Name, c);
            if (f.Method == null) throw Fail($"'{c.Name}' is a field, not a method", c);
            return f.Method.Ret.Name;
        }

        private static string BuiltinType(string key)
        {
            return key == "Keys.Read" || key == "Screen.Rgb" || key.StartsWith("Math.") ? "int" : "void";
        }

        private string NamedCallType(SharpNode c)
        {
            if (Intrinsics.TryGetValue(c.Name, out var intrinsic)) return intrinsic;
            var own = fn?.Cls != null ? classes.Get(fn.Cls) : null;
            if (own != null && own.Methods.Has(c.Name)) return own.Methods.Get(c.Name).Ret.Name;
            var f = functions.Get(c.Name);
            if (f == null) throw Fail($"there is no method '{c.Name}'", c);
            return f.Ret.Name;
        }

        private bool IsSimple(SharpNode e)
        {
            if (ConstValue(e) != null) return true;
            return e.K switch
            {
                "str" => true,
                "name" => IsSimpleName(e),
                "this" => true,
                "cast" => IsSimple(e.E),
                _ => false,
            };
        }

        private bool IsSimpleName(SharpNode e)
        {
            var v = Lookup(e.Name, e);
            return v.Kind == "local" || v.Kind == "global" || v.Kind == "const";
        }
    }
}
