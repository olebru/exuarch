using System;
using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        private static readonly Dictionary<string, string> Inverse = new Dictionary<string, string> { ["=="] = "!=", ["!="] = "==", ["<"] = ">=", [">="] = "<", [">"] = "<=", ["<="] = ">" };

        private static readonly Dictionary<string, Action<SharpGenerator, SharpNode>> StatementKinds = new Dictionary<string, Action<SharpGenerator, SharpNode>>
        {
            ["block"] = (g, s) => g.GenBlock(s),
            ["empty"] = (g, s) => { },
            ["var"] = (g, s) => g.GenDeclaration(s),
            ["exprs"] = (g, s) => g.GenEffects(s.Exprs),
            ["expr"] = (g, s) => g.GenEffect(s.E),
            ["if"] = (g, s) => g.GenIf(s),
            ["while"] = (g, s) => g.GenWhile(s),
            ["do"] = (g, s) => g.GenDo(s),
            ["for"] = (g, s) => g.GenFor(s),
            ["foreach"] = (g, s) => g.GenForeach(s),
            ["switch"] = (g, s) => g.GenSwitch(s),
            ["break"] = (g, s) => g.GenBreak(s),
            ["continue"] = (g, s) => g.GenContinue(s),
            ["return"] = (g, s) => g.GenReturn(s),
        };

        private void GenStatement(SharpNode s)
        {
            At(s);
            if (!StatementKinds.TryGetValue(s.K, out var gen)) throw Fail("unsupported statement", s);
            gen(this, s);
        }

        private void GenBlock(SharpNode s)
        {
            PushScope();
            GenBlockBody(s.Statements);
            PopScope();
        }

        private void GenEffects(List<SharpNode> es)
        {
            foreach (var e in es) GenEffect(e);
        }

        private void GenIf(SharpNode s)
        {
            var ct = TypeOf(s.C);
            if (ct != "bool") throw Fail($"a condition must be a bool, not {ct}", s.C);
            var cv = ConstValue(s.C);
            if (cv != null) { GenConstantIf(s, cv.Value); return; }
            var elseL = NewLabel("else");
            var endL = s.Else != null ? NewLabel("endif") : elseL;
            GenBranch(s.C, elseL, false);
            GenStatement(s.Body);
            if (s.Else != null)
            {
                Emit("JMP", endL);
                Label(elseL);
                GenStatement(s.Else);
            }
            Label(endL);
        }

        private void GenConstantIf(SharpNode s, int cv)
        {
            if (cv != 0) GenStatement(s.Body);
            else if (s.Else != null) GenStatement(s.Else);
        }

        private void GenWhile(SharpNode s)
        {
            string top = NewLabel("loop"), cond = NewLabel("test"), end = NewLabel("done");
            Emit("JMP", cond);
            Label(top);
            Loop(end, cond, () => GenStatement(s.Body));
            At(s);
            Label(cond);
            GenCondJump(s.C, top);
            Label(end);
        }

        private void GenDo(SharpNode s)
        {
            string top = NewLabel("loop"), cond = NewLabel("test"), end = NewLabel("done");
            Label(top);
            Loop(end, cond, () => GenStatement(s.Body));
            Label(cond);
            curLine = s.C.Line;
            GenCondJump(s.C, top);
            Label(end);
        }

        private void GenFor(SharpNode s)
        {
            PushScope();
            if (s.Init != null) GenStatement(s.Init);
            string top = NewLabel("loop"), next = NewLabel("next"), cond = NewLabel("test"), end = NewLabel("done");
            At(s);
            Emit("JMP", cond);
            Label(top);
            Loop(end, next, () => GenStatement(s.Body));
            curLine = 0;
            At(s);
            Label(next);
            foreach (var u in s.Update) GenEffect(u);
            Label(cond);
            if (s.C != null) GenCondJump(s.C, top);
            else Emit("JMP", top);
            Label(end);
            PopScope();
        }

        private void GenBreak(SharpNode s)
        {
            if (fn.Loops.Count == 0) throw Fail("'break' outside a loop or switch", s);
            Emit("JMP", fn.Loops[fn.Loops.Count - 1].Brk);
        }

        private void GenContinue(SharpNode s)
        {
            var l = Enumerable.Reverse(fn.Loops).FirstOrDefault(x => x.Cont != null);
            if (l.Cont == null) throw Fail("'continue' outside a loop", s);
            Emit("JMP", l.Cont);
        }

        private void GenReturn(SharpNode s)
        {
            if (s.E != null)
            {
                if (fn.Ret == "void") throw Fail($"{fn.Name} is void and can not return a value", s.E);
                GenValue(s.E, fn.Ret, s.E);
            }
            else if (fn.Ret != "void") throw Fail($"{fn.Name} must return {An(fn.Ret)}", s);
            Emit("JMP", fn.RetLabel);
        }

        private void Loop(string brk, string cont, Action body)
        {
            fn.Loops.Add((brk, cont));
            body();
            fn.Loops.RemoveAt(fn.Loops.Count - 1);
        }

        private void GenDeclaration(SharpNode s)
        {
            foreach (var d in s.Decls)
            {
                if (s.IsConst) DeclareConst(s, d);
                else DeclareVariable(s, d);
            }
        }

        private void DeclareConst(SharpNode s, SharpDecl d)
        {
            var type = s.Type.Name;
            var v = ConstValue(d.Init);
            if (v == null) throw Fail($"the value of const '{d.Name}' must be a constant", d.Init);
            if (InScope(d.Name)) throw Fail($"'{d.Name}' is already defined in this method", d);
            scope.Vars[d.Name] = new SharpSymbol { Kind = "const", Value = v.Value, Type = type == "var" ? TypeOf(d.Init) : type };
        }

        private void DeclareVariable(SharpNode s, SharpDecl d)
        {
            var type = s.Type.Name;
            if (type == "var") type = InferredType(d);
            if (d.Init != null)
            {
                At(d);
                GenValue(d.Init, type, d);
            }
            var v = DeclareLocal(d.Name, type, d);
            NoteLocal(v);
            if (d.Init != null) Emit("STL", Number(v.Offset));
        }

        private string InferredType(SharpDecl d)
        {
            if (d.Init.K == "arrinit") throw Fail("'var' needs a type for an array initializer: write int[] or new[] { … }", d.Init);
            var type = TypeOf(d.Init);
            if (type == "null" || type == "void") throw Fail($"can not infer a type from {type}", d.Init);
            return type;
        }

        private void GenForeach(SharpNode s)
        {
            PushScope();
            var collType = TypeOf(s.Collection);
            if (!IsArray(collType) && collType != "string") throw Fail($"foreach works on arrays and strings, not {collType}", s.Collection);
            var elemType = collType == "string" ? "char" : ElementOf(collType);
            var ptr = DeclareLocal($"__each{Number(labelCount)}", collType, s);
            var idx = DeclareLocal($"__index{Number(labelCount)}", "int", s);
            GenExpr(s.Collection);
            Emit("STL", Number(ptr.Offset));
            Emit("LDI", "0");
            Emit("STL", Number(idx.Offset));
            string top = NewLabel("loop"), next = NewLabel("next"), cond = NewLabel("test"), end = NewLabel("done");
            Emit("JMP", cond);
            Label(top);
            ForeachBody(s, collType, elemType, ptr, idx, end, next);
            At(s);
            Label(next);
            Emit("LDL", Number(idx.Offset)); Emit("INC"); Emit("STL", Number(idx.Offset));
            Label(cond);
            ForeachTest(collType, ptr, idx, top);
            Label(end);
            PopScope();
        }

        private void ForeachBody(SharpNode s, string collType, string elemType, SharpSymbol ptr, SharpSymbol idx, string end, string next)
        {
            PushScope();
            var x = DeclareLocal(s.Name, s.Type.Name == "var" ? elemType : s.Type.Name, s);
            NoteLocal(x);
            Emit("LDL", Number(ptr.Offset)); Emit("TAB"); Emit("LDL", Number(idx.Offset)); Emit("ADD");
            if (collType != "string") Emit("INC");
            Emit("LDX");
            Emit("STL", Number(x.Offset));
            Loop(end, next, () => GenStatement(s.Body));
            PopScope();
        }

        private void ForeachTest(string collType, SharpSymbol ptr, SharpSymbol idx, string top)
        {
            if (collType == "string")
            {
                Emit("LDL", Number(ptr.Offset)); Emit("TAB"); Emit("LDL", Number(idx.Offset)); Emit("ADD"); Emit("LDX");
                Emit("TST"); Emit("JNE", top);
                return;
            }
            Emit("LDL", Number(idx.Offset)); Emit("TAB"); Emit("LDL", Number(ptr.Offset)); Emit("LDX");
            Emit("CMP"); Emit("JLT", top);
        }

        private void GenSwitch(SharpNode s)
        {
            var t = TypeOf(s.E);
            if (!IsInt(t) && t != "bool") throw Fail($"switch works on int, char and bool, not {t}", s.E);
            GenExpr(s.E);
            Emit("TAB");
            var end = NewLabel("endswitch");
            string defaultLabel = null;
            var seen = new HashSet<int>();
            var sectionLabels = new List<string>();
            foreach (var section in s.Sections)
            {
                var l = NewLabel("case");
                foreach (var lab in section.Labels)
                {
                    if (lab.IsDefault) defaultLabel = l;
                    else GenCaseTest(lab, t, l, seen);
                }
                sectionLabels.Add(l);
            }
            Emit("JMP", defaultLabel ?? end);
            GenSwitchSections(s, sectionLabels, end);
            Label(end);
        }

        private void GenCaseTest(SharpCaseLabel lab, string t, string l, HashSet<int> seen)
        {
            var v = ConstValue(lab.E);
            if (v == null) throw Fail("a case label must be a constant", lab.E);
            if (!seen.Add(v.Value)) throw Fail($"case {Number(v.Value)} appears twice", lab.E);
            Emit("LDI", t == "char" ? AsmChar(v.Value & 0xffff) : Number(v.Value));
            Emit("CMP");
            Emit("JEQ", l);
        }

        private void GenSwitchSections(SharpNode s, List<string> sectionLabels, string end)
        {
            fn.Loops.Add((end, null));
            for (int i = 0; i < s.Sections.Count; i++)
            {
                Label(sectionLabels[i]);
                PushScope();
                GenBlockBody(s.Sections[i].Body);
                PopScope();
            }
            fn.Loops.RemoveAt(fn.Loops.Count - 1);
        }

        private void GenCondJump(SharpNode c, string label) => GenBranch(c, label, true);

        private void GenBranch(SharpNode e, string label, bool jumpIf)
        {
            var t = TypeOf(e);
            if (t != "bool") throw Fail($"a condition must be a bool, not {t}", e);
            if (ConstValue(e) is { } cv)
            {
                GenConstantBranch(cv, label, jumpIf);
                return;
            }
            if (e.K == "unary" && e.Op == "!") { GenBranch(e.E, label, !jumpIf); return; }
            if (e.K == "bin" && LogicalOps.Contains(e.Op)) { GenLogicalBranch(e, label, jumpIf); return; }
            if (e.K == "bin" && IsComparison(e.Op)) { GenComparisonBranch(e, label, jumpIf); return; }
            GenExpr(e);
            Emit("TST");
            Emit(jumpIf ? "JNE" : "JEQ", label);
        }

        private void GenConstantBranch(int cv, string label, bool jumpIf)
        {
            if ((cv != 0) == jumpIf) Emit("JMP", label);
        }

        private void GenLogicalBranch(SharpNode e, string label, bool jumpIf)
        {
            bool and = e.Op == "&&";
            if (and != jumpIf)
            {
                GenBranch(e.L, label, jumpIf);
                GenBranch(e.R, label, jumpIf);
                return;
            }
            var skip = NewLabel("skip");
            GenBranch(e.L, skip, !jumpIf);
            GenBranch(e.R, label, jumpIf);
            Label(skip);
        }

        private void GenComparisonBranch(SharpNode e, string label, bool jumpIf)
        {
            var op = jumpIf ? e.Op : Inverse[e.Op];
            CheckComparable(e, TypeOf(e.L), TypeOf(e.R));
            GenCompare(e);
            Emit(JumpFor[op], label);
        }

        private void GenCompare(SharpNode e)
        {
            if (ConstValue(e.R) == 0 || e.R.K == "null")
            {
                GenExpr(e.L);
                Emit("TST");
                return;
            }
            GenOperands(e.L, e.R, false);
            Emit("CMP");
        }

        private void CheckComparable(SharpNode e, string lt, string rt)
        {
            if (lt == "string" || rt == "string") throw Fail("comparing strings is not supported", e);
            if (IsRef(lt) || IsRef(rt) || lt == "null" || rt == "null") { CheckReferenceComparison(e, lt, rt); return; }
            if ((lt == "bool") != (rt == "bool")) throw Fail($"can not compare {lt} and {rt}", e);
            if (lt == "bool" && !EqualityOps.Contains(e.Op)) throw Fail($"'{e.Op}' does not work on bool", e);
        }

        private void CheckReferenceComparison(SharpNode e, string lt, string rt)
        {
            if (e.Op != "==" && e.Op != "!=") throw Fail($"'{e.Op}' does not work on {(IsRef(lt) ? lt : rt)}: references can only be compared with == and !=", e);
            if (lt != rt && lt != "null" && rt != "null") throw Fail($"can not compare {An(lt)} with {An(rt)}", e);
        }

        private void GenOperands(SharpNode l, SharpNode r, bool commutative)
        {
            if (IsSimple(r))
            {
                GenExpr(l);
                Emit("TAB");
                GenExpr(r);
                return;
            }
            if (commutative && ConstValue(l) != null)
            {
                GenExpr(r);
                Emit("TAB");
                GenExpr(l);
                return;
            }
            GenExpr(l);
            Emit("PUSH");
            GenExpr(r);
            Emit("POP");
        }
    }
}
