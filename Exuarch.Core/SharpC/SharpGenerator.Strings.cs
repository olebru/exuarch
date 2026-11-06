using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private const string StringMethods = "Length, s[i] and ToCharArray()";

        private readonly HashSet<string> stringRoutines = new HashSet<string>();
        private readonly List<string> stringRuntime = new List<string>();

        public IReadOnlyList<string> StringRuntime => stringRuntime;

        private bool IsTraced(string t) => IsRef(t) || t == "string";

        private void CallStringRoutine(string name)
        {
            NeedsHeap = true;
            stringRoutines.Add(name);
            Emit("CALL", name);
        }

        private string GenStringJoin(SharpNode e)
        {
            var pieces = JoinPieces(e);
            if (pieces.Count == 0) pieces.Add("");
            GenPiece(pieces[0]);
            foreach (var piece in pieces.Skip(1)) AppendPiece(piece);
            return "string";
        }

        private void AppendPiece(object piece)
        {
            Emit("PUSH");
            GenPiece(piece);
            Emit("PUSH");
            CallStringRoutine("__Concat");
        }

        private List<object> JoinPieces(SharpNode e)
        {
            var nodes = new List<SharpNode>();
            FlattenJoin(e, nodes);
            var pieces = new List<object>();
            string text = null;
            foreach (var node in nodes)
            {
                if (ConstPiece(node) is { } constant)
                {
                    text = (text ?? "") + constant;
                    continue;
                }
                if (!string.IsNullOrEmpty(text)) pieces.Add(text);
                text = null;
                pieces.Add(node);
            }
            if (!string.IsNullOrEmpty(text)) pieces.Add(text);
            return pieces;
        }

        private void FlattenJoin(SharpNode e, List<SharpNode> into)
        {
            if (IsStringJoin(e))
            {
                FlattenJoin(e.L, into);
                FlattenJoin(e.R, into);
            }
            else if (e.K == "interp")
            {
                foreach (var part in e.Parts) into.Add(part as SharpNode ?? new SharpNode { K = "str", Text = (string)part, Line = e.Line, Col = e.Col });
            }
            else into.Add(e);
        }

        private bool IsStringJoin(SharpNode e) => e.K == "bin" && e.Op == "+" && TypeOf(e) == "string";

        private string ConstPiece(SharpNode e)
        {
            if (e.K == "str") return e.Text;
            if (e.K == "null") return "";
            var t = TypeOf(e);
            if (t == "string" || ConstValue(e) is not { } v) return null;
            return t switch
            {
                "char" => ((char)(v & 0xffff)).ToString(),
                "bool" => v != 0 ? "True" : "False",
                _ => Number(v),
            };
        }

        private void GenPiece(object piece)
        {
            if (piece is string text)
            {
                Emit("LEA", StringLabel(text));
                return;
            }
            var node = (SharpNode)piece;
            var t = GenExpr(node);
            switch (t)
            {
                case "string":
                case "null":
                    return;
                case "char":
                    Emit("PUSH");
                    CallStringRoutine("__CharString");
                    return;
                case "int":
                    Emit("PUSH");
                    CallStringRoutine("__IntString");
                    return;
                case "bool":
                    GenBoolText();
                    return;
                default:
                    throw Fail($"can not join {An(t)} to a string", node);
            }
        }

        private void GenBoolText()
        {
            string no = NewLabel("false"), end = NewLabel("text");
            Emit("TST");
            Emit("JEQ", no);
            Emit("LEA", StringLabel("True"));
            Emit("JMP", end);
            Label(no);
            Emit("LEA", StringLabel("False"));
            Label(end);
        }

        private string GenStringAppend(SharpNode target, string binop, SharpNode value)
        {
            if (binop != "+") throw Fail($"'{binop}=' does not work on a string", value);
            GenAddress(target);
            Emit("PUSH");
            Emit("LDX");
            foreach (var piece in JoinPieces(value)) AppendPiece(piece);
            Emit("POP");
            Emit("STX");
            return "string";
        }

        private string GenConcatBuiltin(SharpNode e)
        {
            if (e.Args.Count == 0) return GenStringJoin(EmptyString(e));
            var first = TypeOf(e.Args[0]) == "string" ? e.Args[0] : Bin("+", EmptyString(e), e.Args[0], e.Args[0]);
            return GenStringJoin(e.Args.Skip(1).Aggregate(first, (l, r) => Bin("+", l, r, r)));
        }

        private static SharpNode EmptyString(SharpPos at) => new SharpNode { K = "str", Text = "", Line = at.Line, Col = at.Col };

        private string GenNewString(SharpNode e)
        {
            if (e.Args.Count == 1)
            {
                var t = GenExpr(e.Args[0]);
                if (t != "char[]" && t != "null") throw Fail($"new string(…) needs a char[], not {An(t)}", e.Args[0]);
                Emit("PUSH");
                CallStringRoutine("__StringFromChars");
                return "string";
            }
            if (e.Args.Count != 2) throw Fail("new string(…) takes a char[], or a char and a count", e);
            var ct = GenExpr(e.Args[0]);
            if (ct != "char") throw Fail($"new string(c, count) needs a char first, not {An(ct)}", e.Args[0]);
            Emit("PUSH");
            GenInt(e.Args[1]);
            Emit("PUSH");
            CallStringRoutine("__RepeatChar");
            return "string";
        }

        private bool IsStringValue(SharpNode obj)
        {
            if (obj.K == "name" && !IsVariable(obj.Name)) return false;
            return TypeOf(obj) == "string";
        }

        private string GenStringMethod(SharpNode e, SharpNode c)
        {
            StringMethodType(c);
            Args(e, 0, "ToCharArray");
            GenExpr(c.Obj);
            Emit("PUSH");
            CallStringRoutine("__ToCharArray");
            return "char[]";
        }

        private static string StringMethodType(SharpNode c)
        {
            if (c.Name != "ToCharArray") throw Fail($"'{c.Name}' is not available on a string. SharpC strings have {StringMethods}", c);
            return "char[]";
        }

        private bool IsStringComparison(SharpNode e) => TypeOf(e.L) == "string" && TypeOf(e.R) == "string";

        private void CheckStringComparison(SharpNode e, string lt, string rt)
        {
            if (!EqualityOps.Contains(e.Op)) throw Fail($"'{e.Op}' does not work on strings: they can only be compared with == and !=", e);
            var other = lt == "string" ? rt : lt;
            if (other != "string" && other != "null") throw Fail($"can not compare a string with {An(other)}", e);
        }

        private void GenStringCompare(SharpNode e)
        {
            GenExpr(e.L);
            Emit("PUSH");
            GenExpr(e.R);
            Emit("PUSH");
            CallStringRoutine("__StringsDiffer");
            Emit("TST");
        }
    }
}
