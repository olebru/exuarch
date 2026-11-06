using System;
using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private const int ArrayOfValues = 1;
        private const int ArrayOfReferences = 2;

        private static readonly Dictionary<string, string> Arith = new Dictionary<string, string>
        {
            ["+"] = "ADD", ["-"] = "SUB", ["*"] = "MUL", ["/"] = "DIV", ["%"] = "MOD", ["&"] = "AND", ["|"] = "OR", ["^"] = "XOR", ["<<"] = "SHL", [">>>"] = "SHR",
        };

        private static readonly HashSet<string> Commutative = new HashSet<string> { "+", "*", "&", "|", "^" };
        private static readonly HashSet<string> IdentityWithZero = new HashSet<string> { "+", "-", "|", "^", "<<", ">>", ">>>" };

        private static readonly Dictionary<string, Func<SharpGenerator, SharpNode, string>> ExpressionKinds = new Dictionary<string, Func<SharpGenerator, SharpNode, string>>
        {
            ["str"] = (g, e) => g.GenString(e),
            ["interp"] = (g, e) => g.GenStringJoin(e),
            ["null"] = (g, e) => g.GenNull(),
            ["this"] = (g, e) => g.GenThis(e),
            ["newobj"] = (g, e) => g.GenNewObject(e),
            ["newstr"] = (g, e) => g.GenNewString(e),
            ["name"] = (g, e) => g.GenName(e),
            ["member"] = (g, e) => g.GenMember(e),
            ["index"] = (g, e) => g.GenIndex(e),
            ["cast"] = (g, e) => g.GenCast(e),
            ["unary"] = (g, e) => g.GenUnary(e),
            ["pre"] = (g, e) => g.GenIncDec(e, true, false),
            ["post"] = (g, e) => g.GenIncDec(e, false, false),
            ["bin"] = (g, e) => g.GenBinary(e),
            ["assign"] = (g, e) => g.GenAssign(e),
            ["cond"] = (g, e) => g.GenConditional(e),
            ["call"] = (g, e) => g.GenCallValue(e),
            ["newarr"] = (g, e) => g.GenNewArray(e),
            ["arrinit"] = (g, e) => throw Fail("an array initializer can only be used to set up an array variable", e),
        };

        private static string AsmChar(int v)
        {
            if (v >= 32 && v < 127 && v != 39 && v != 92) return $"'{(char)v}'";
            return Number(v);
        }

        private string GenValue(SharpNode e, string type, SharpPos node)
        {
            if (e.K == "arrinit")
            {
                if (!IsArray(type)) throw Fail($"an array initializer needs an array type, not {type}", e);
                return GenArray(ElementOf(type), null, e);
            }
            var t = GenExpr(e);
            CheckAssignable(type, t, node ?? e, e);
            return t;
        }

        private void CheckAssignable(string to, string from, SharpPos node, SharpNode value)
        {
            if (to == from || (to == "int" && from == "char")) return;
            if (to == "char" && from == "int")
            {
                CheckCharFromInt(node, value);
                return;
            }
            if ((IsRef(to) || to == "string") && from == "null") return;
            throw Fail($"can not use {An(from)} where {An(to)} is needed", node);
        }

        private void CheckCharFromInt(SharpPos node, SharpNode value)
        {
            if (ConstValue(value) != null) return;
            throw Fail("can not put an int in a char without a cast: write (char)", node);
        }

        private void GenEffect(SharpNode e)
        {
            if (e.K == "post") { GenIncDec(e, false, true); return; }
            if (e.K == "pre") { GenIncDec(e, true, true); return; }
            if (e.K == "call") { GenCall(e); return; }
            GenExpr(e);
        }

        private string GenExpr(SharpNode e)
        {
            var cv = ConstValue(e);
            if (cv != null && e.K != "name")
            {
                var t = TypeOf(e);
                Emit("LDI", t == "char" ? AsmChar(cv.Value & 0xffff) : Number(cv.Value));
                return t;
            }
            if (!ExpressionKinds.TryGetValue(e.K, out var gen)) throw Fail("this expression is not supported", e);
            return gen(this, e);
        }

        private string GenString(SharpNode e)
        {
            Emit("LEA", StringLabel(e.Text));
            return "string";
        }

        private string GenNull()
        {
            Emit("LDI", "0");
            return "null";
        }

        private string GenThis(SharpNode e)
        {
            ThisType(e);
            Emit("LDL", Number(Lookup("this", e).Offset));
            return fn.Cls;
        }

        private string GenName(SharpNode e)
        {
            var v = Lookup(e.Name, e);
            if (v.Kind == "field") return GenExpr(FieldNode(e));
            if (v.Kind == "const") Emit("LDI", v.Type == "char" ? AsmChar(v.Value & 0xffff) : Number(v.Value));
            else if (v.Kind == "local") Emit("LDL", Number(v.Offset));
            else Emit("LD", v.Label);
            return v.Type;
        }

        private string GenMember(SharpNode e)
        {
            var target = MemberTarget(e.Obj);
            if (target?.Static != null) return GenStaticField(target.Static, e);
            if (target?.Instance != null)
            {
                var t = GenFieldAddress(e);
                Emit("LDX");
                return t;
            }
            if (e.Name == "Length" && GenLength(e) is { } length) return length;
            throw Fail($"'{e.Name}' is not supported here", e);
        }

        private string GenStaticField(SharpClassInfo info, SharpNode e)
        {
            var g = StaticMember(info, e.Name, e);
            if (g.Kind != "global") throw Fail($"'{e.Name}' is a method: call it with {e.Name}(…)", e);
            Emit("LD", g.Label);
            return g.Type;
        }

        private string GenLength(SharpNode e)
        {
            var t = GenExpr(e.Obj);
            if (IsArray(t))
            {
                Emit("LDX");
                return "int";
            }
            if (t != "string") return null;
            Use("__strlen");
            Emit("CALL", "__strlen");
            return "int";
        }

        private string GenIndex(SharpNode e)
        {
            var et = GenElementAddress(e);
            Emit("LDX");
            return et;
        }

        private string GenCast(SharpNode e)
        {
            var t = GenExpr(e.E);
            var to = e.Type.Name;
            if (to == t) return t;
            if ((to == "int" || to == "char") && IsInt(t)) return to;
            throw Fail($"can not cast {t} to {to}", e);
        }

        private string GenUnary(SharpNode e)
        {
            var t = GenExpr(e.E);
            if (e.Op == "!") return GenNot(e, t);
            if (!IsInt(t)) throw Fail($"'{e.Op}' needs a number, not {t}", e);
            if (e.Op == "-") Emit("NEG");
            if (e.Op == "~") Emit("NOT");
            return "int";
        }

        private string GenNot(SharpNode e, string t)
        {
            if (t != "bool") throw Fail($"'!' needs a bool, not {t}", e);
            Emit("TAB");
            Emit("LDI", "1");
            Emit("XOR");
            return "bool";
        }

        private string GenConditional(SharpNode e)
        {
            var ct = TypeOf(e.C);
            if (ct != "bool") throw Fail($"the condition of ?: must be a bool, not {ct}", e.C);
            string elseL = NewLabel("else"), end = NewLabel("endif");
            GenBranch(e.C, elseL, false);
            var ta = GenExpr(e.A);
            Emit("JMP", end);
            Label(elseL);
            var tb = GenExpr(e.B);
            Label(end);
            if (ta != tb && !(IsInt(ta) && IsInt(tb)) && ta != "null" && tb != "null") throw Fail($"the two sides of ?: are {ta} and {tb}", e);
            if (ta == "null") return tb;
            return ta == tb ? ta : "int";
        }

        private string GenCallValue(SharpNode e)
        {
            var t = GenCall(e);
            if (t == "void") throw Fail("this method returns nothing (void), so it has no value to use", e);
            return t;
        }

        private string GenNewArray(SharpNode e)
        {
            if (e.Init != null) return GenArray(e.Elem, e.Size, e.Init);
            var st = GenExpr(e.Size);
            if (!IsInt(st)) throw Fail($"an array size must be an int, not {st}", e.Size);
            Emit("PUSH");
            CallNewArray(e.Elem);
            return e.Elem + "[]";
        }

        private string GenArray(string elem, SharpNode sizeExpr, SharpNode init)
        {
            int n = init.Items.Count;
            if (sizeExpr != null) CheckArraySize(sizeExpr, n);
            Emit("LDI", Number(n));
            Emit("PUSH");
            CallNewArray(elem);
            int tmp = -(1 + NewSlot());
            Emit("STL", Number(tmp));
            for (int i = 0; i < n; i++) GenArrayItem(elem, init.Items[i], i, tmp);
            Emit("LDL", Number(tmp));
            return elem + "[]";
        }

        private void CheckArraySize(SharpNode sizeExpr, int n)
        {
            var sv = ConstValue(sizeExpr);
            if (sv == n) return;
            throw Fail($"the array has {Number(n)} element{(n == 1 ? "" : "s")}, but its size says {(sv == null ? "something else" : Number(sv.Value))}", sizeExpr);
        }

        private void GenArrayItem(string elem, SharpNode item, int i, int tmp)
        {
            Emit("LDL", Number(tmp));
            Emit("TAB");
            Emit("LDI", Number(i + 1));
            Emit("ADD");
            if (IsSimple(item))
            {
                Emit("TAB");
                GenValue(item, elem, item);
            }
            else
            {
                Emit("PUSH");
                GenValue(item, elem, item);
                Emit("POP");
            }
            Emit("STX");
        }

        private void CallNewArray(string elem)
        {
            NeedsHeap = true;
            Emit("LDI", Number(IsTraced(elem) ? ArrayOfReferences : ArrayOfValues));
            Emit("PUSH");
            Emit("CALL", "__NewArray");
        }

        private string GenNewObject(SharpNode e)
        {
            var info = classes.Get(e.Cls);
            NeedsHeap = true;
            if (!instantiated.Contains(e.Cls)) instantiated.Add(e.Cls);
            Emit("LDI", Number(info.Size));
            Emit("PUSH");
            Emit("LEA", $"__d_{e.Cls}");
            Emit("PUSH");
            Emit("CALL", "__New");
            if (info.Ctor != null)
            {
                Emit("PUSH");
                GenArgs(info.Ctor, e.Args, e, $"new {e.Cls}");
                Emit("CALL", info.Ctor.Label);
            }
            else if (e.Args.Count > 0) throw Fail($"{e.Cls} has no constructor, so new {e.Cls}() takes no arguments", e);
            return e.Cls;
        }

        private string GenFieldAddress(SharpNode e)
        {
            var info = classes.Get(TypeOf(e.Obj));
            var f = InstanceMember(info, e.Name, e);
            if (f.Kind != "field") throw Fail($"'{e.Name}' is a method: call it with {e.Name}(…)", e);
            GenExpr(e.Obj);
            if (f.Offset == 1) Emit("INC");
            else if (f.Offset != 0)
            {
                Emit("TAB");
                Emit("LDI", Number(f.Offset));
                Emit("ADD");
            }
            return f.Type;
        }

        private string GenAddress(SharpNode e) => e.K == "index" ? GenElementAddress(e) : GenFieldAddress(e);

        private SharpNode Lvalue(SharpNode target)
        {
            if (target.K == "name") return Lookup(target.Name, target).Kind == "field" ? FieldNode(target) : target;
            if (target.K != "member") return target;
            var t = MemberTarget(target.Obj);
            if (t?.Static != null) return StaticLvalue(t.Static, target);
            if (t == null) throw Fail($"'{target.Name}' can not be assigned", target);
            return target;
        }

        private SharpNode StaticLvalue(SharpClassInfo info, SharpNode target)
        {
            var g = StaticMember(info, target.Name, target);
            if (g.Kind == "const") throw Fail($"'{target.Name}' is a constant and can not be changed", target);
            if (g.Kind != "global") throw Fail($"'{target.Name}' is a method and can not be assigned", target);
            return new SharpNode { K = "name", Name = target.Name, Line = target.Line, Col = target.Col };
        }

        private string GenElementAddress(SharpNode e)
        {
            var t = TypeOf(e.Obj);
            if (!IsArray(t) && t != "string") throw Fail($"only arrays and strings can be indexed, not {t}", e);
            var it = TypeOf(e.Index);
            if (!IsInt(it)) throw Fail($"an index must be an int, not {it}", e.Index);
            var iv = ConstValue(e.Index);
            int extra = t == "string" ? 0 : 1;
            if (iv != null) GenConstantElementAddress(e, iv.Value + extra);
            else
            {
                GenOperands(e.Obj, e.Index, true);
                Emit("ADD");
                if (extra != 0) Emit("INC");
            }
            return t == "string" ? "char" : ElementOf(t);
        }

        private void GenConstantElementAddress(SharpNode e, int offset)
        {
            GenExpr(e.Obj);
            if (offset == 1) Emit("INC");
            else if (offset != 0)
            {
                Emit("TAB");
                Emit("LDI", Number(offset));
                Emit("ADD");
            }
        }
    }
}
