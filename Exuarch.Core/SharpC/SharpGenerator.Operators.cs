namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private string GenBinary(SharpNode e)
        {
            if (IsComparison(e.Op)) return GenComparisonValue(e);
            if (LogicalOps.Contains(e.Op)) return GenLogicalValue(e);
            var lt = TypeOf(e.L);
            var rt = TypeOf(e.R);
            if (e.Op == "+" && (lt == "string" || rt == "string")) return GenStringJoin(e);
            bool bools = lt == "bool" && rt == "bool" && BitwiseOps.Contains(e.Op);
            if (!bools) CheckNumbers(e, lt, rt);
            return GenArithmetic(e, bools);
        }

        private static void CheckNumbers(SharpNode e, string lt, string rt)
        {
            if (!IsInt(lt)) throw Fail($"'{e.Op}' needs numbers, but the left side is {lt}", e.L);
            if (!IsInt(rt)) throw Fail($"'{e.Op}' needs numbers, but the right side is {rt}", e.R);
        }

        private string GenArithmetic(SharpNode e, bool bools)
        {
            var op = e.Op;
            var rv = ConstValue(e.R);
            if (StepInstruction(op, rv) is { } step)
            {
                GenExpr(e.L);
                Emit(step);
                return "int";
            }
            if (IdentityWithZero.Contains(op) && rv == 0)
            {
                GenExpr(e.L);
                return "int";
            }
            if (op == ">>")
            {
                GenOperands(e.L, e.R, false);
                Use("__sar");
                Emit("CALL", "__sar");
                return "int";
            }
            GenOperands(e.L, e.R, Commutative.Contains(op));
            Emit(Arith[op]);
            return bools ? "bool" : "int";
        }

        private string GenComparisonValue(SharpNode e)
        {
            CheckComparable(e, TypeOf(e.L), TypeOf(e.R));
            GenCompare(e);
            var yes = NewLabel("true");
            Emit("LDI", "1");
            Emit(JumpFor[e.Op], yes);
            Emit("LDI", "0");
            Label(yes);
            return "bool";
        }

        private string GenLogicalValue(SharpNode e)
        {
            string no = NewLabel("false"), end = NewLabel("bool");
            GenBranch(e, no, false);
            Emit("LDI", "1");
            Emit("JMP", end);
            Label(no);
            Emit("LDI", "0");
            Label(end);
            return "bool";
        }

        private string GenIncDec(SharpNode e, bool prefix, bool discard)
        {
            var target = Lvalue(e.E);
            var ins = e.Op == "++" ? "INC" : "DEC";
            var undo = e.Op == "++" ? "DEC" : "INC";
            var t = TypeOf(target);
            if (!IsInt(t)) throw Fail($"'{e.Op}' needs a number, not {t}", e);
            if (target.K == "name") GenIncDecVariable(target, ins);
            else if (target.K == "index" || target.K == "member") GenIncDecElement(target, ins);
            else throw Fail($"'{e.Op}' needs a variable, a field or an array element", e);
            if (!prefix && !discard) Emit(undo);
            return t;
        }

        private void GenIncDecVariable(SharpNode target, string ins)
        {
            var v = Lookup(target.Name, target);
            if (v.Kind == "const") throw Fail($"'{target.Name}' is a constant", target);
            GenExpr(target);
            Emit(ins);
            Store(v);
        }

        private void GenIncDecElement(SharpNode target, string ins)
        {
            if (target.K == "index" && TypeOf(target.Obj) == "string") throw Fail("strings can not be changed", target);
            GenAddress(target);
            Emit("PUSH");
            Emit("LDX");
            Emit(ins);
            Emit("POP");
            Emit("STX");
        }

        private void Store(SharpSymbol v)
        {
            if (v.Kind == "local") Emit("STL", Number(v.Offset));
            else if (v.Kind == "global") Emit("ST", v.Label);
            else throw Fail("can not assign to a constant", null);
        }

        private string GenAssign(SharpNode e)
        {
            var target = Lvalue(e.Target);
            var tt = TypeOf(target);
            var binop = e.Op == "=" ? null : e.Op.Substring(0, e.Op.Length - 1);
            if (target.K == "name") return GenAssignVariable(e, target, tt, binop);
            if (target.K == "index" && TypeOf(target.Obj) == "string") throw Fail("strings can not be changed", target);
            if (tt == "string" && binop != null) return GenStringAppend(target, binop, e.E);
            GenAddress(target);
            if (binop == null) GenStoredValue(e.E, tt);
            else
            {
                Emit("PUSH");
                Emit("LDX");
                GenCompoundOnA(binop, e.E, tt);
                Emit("POP");
            }
            Emit("STX");
            return tt;
        }

        private void GenStoredValue(SharpNode value, string tt)
        {
            if (IsSimple(value))
            {
                Emit("TAB");
                GenValue(value, tt, value);
                return;
            }
            Emit("PUSH");
            GenValue(value, tt, value);
            Emit("POP");
        }

        private string GenAssignVariable(SharpNode e, SharpNode target, string tt, string binop)
        {
            var v = Lookup(target.Name, target);
            if (v.Kind == "const") throw Fail($"'{target.Name}' is a constant and can not be changed", target);
            if (binop == null) GenValue(e.E, tt, e.E);
            else GenCompound(target, binop, e.E, tt);
            Store(v);
            return tt;
        }

        private void GenCompound(SharpNode target, string binop, SharpNode value, string tt)
        {
            var node = new SharpNode { K = "bin", Op = binop, L = target, R = value, Line = value.Line, Col = value.Col };
            var t = GenBinary(node);
            if (t == "string" && tt != "string") throw Fail($"can not use a string where {An(tt)} is needed", value);
            if (tt == "char" && t == "int") return;
            if (tt == "bool" && t != "bool") throw Fail($"can not use '{binop}=' on a bool", value);
        }

        private void GenCompoundOnA(string binop, SharpNode value, string tt)
        {
            var vt = TypeOf(value);
            if (!IsInt(vt) && !(tt == "bool" && vt == "bool")) throw Fail($"'{binop}=' needs a number, not {vt}", value);
            if (StepInstruction(binop, ConstValue(value)) is { } step)
            {
                Emit(step);
                return;
            }
            if (IsSimple(value))
            {
                Emit("TAB");
                GenExpr(value);
            }
            else
            {
                Emit("PUSH");
                GenExpr(value);
                Emit("POP");
            }
            if (binop == ">>") GenShiftRight();
            else Emit(Arith[binop]);
        }

        private static string StepInstruction(string op, int? rv)
        {
            if ((op != "+" && op != "-") || (rv != 1 && rv != -1)) return null;
            return (op == "+") == (rv == 1) ? "INC" : "DEC";
        }

        private void GenShiftRight()
        {
            Use("__sar");
            Emit("CALL", "__sar");
        }
    }
}
