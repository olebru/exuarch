using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Exuarch.Core.SharpC
{
    internal sealed partial class SharpGenerator
    {
        public List<SharpOutLine> Generate()
        {
            DeclareTopLevel();
            var main = functions.Get("Main");
            Comment("SHARP-16 assembly, compiled from C# by SharpC", 0);
            Blank();
            bool needsInit = unit.Fields.Any(NeedsInitCode);
            if (needsInit) Emit("CALL", "__init");
            for (int i = 0; i < main.Params.Count; i++) { Emit("LDI", "0"); Emit("PUSH"); }
            Emit("CALL", "Main");
            Emit("HLT");
            int descriptorsAt = output.Count;
            if (needsInit) GenInit();
            GenFunctions();
            if (NeedsHeap) GenHeapRuntime();
            output.InsertRange(descriptorsAt, Descriptors());
            curLine = 0;
            AppendRuntime();
            AppendData();
            Peephole();
            return output;
        }

        private bool NeedsInitCode(SharpField f) => !f.IsConst && f.Init != null && GlobalInitValue(f) == null;

        private void GenFunctions()
        {
            foreach (var f in functions.Values) GenFunction(f);
            foreach (var info in classes.Values)
            {
                if (info.Ctor != null) GenFunction(info.Ctor);
                foreach (var m in info.Methods.Values) GenFunction(m);
            }
        }

        private void AppendRuntime()
        {
            var data = new HashSet<string>();
            foreach (var name in runtime.ToList())
            {
                Blank();
                var routine = SharpRuntime.Routines[name];
                foreach (var line in routine.Code.Trim().Split('\n')) output.Add(new SharpOutLine { Text = line, Line = 0, Kind = line.StartsWith(";") ? "comment" : "runtime" });
                foreach (var d in routine.Data) if (data.Add(d)) dataOrder.Add(d);
            }
        }

        private readonly List<string> dataOrder = new List<string>();

        private void AppendData()
        {
            var globalList = globals.Values.Where(g => g.Kind == "global").OrderBy(g => g.Field.Runtime ? 1 : 0).ToList();
            if (globalList.Count > 0 || strings.Count > 0 || dataOrder.Count > 0)
            {
                Blank();
                Comment("Data", 0);
            }
            foreach (var g in globalList) AppendGlobal(g);
            curLine = 0;
            foreach (var s in strings.KeyList) output.Add(new SharpOutLine { Text = $"{strings.Get(s)}:".PadRight(10) + $".STRING {AsmString(s)}", Line = 0, Kind = "data" });
            foreach (var d in dataOrder) output.Add(new SharpOutLine { Text = SharpRuntime.Data[d], Line = 0, Kind = "data" });
            if (NeedsHeap) AppendHeapData(globalList);
        }

        private void AppendGlobal(SharpSymbol g)
        {
            var v = GlobalInitValue(g.Field);
            int line = g.Field.Runtime ? 0 : g.Field.Line;
            curLine = line;
            output.Add(new SharpOutLine { Text = $"{g.Label}:".PadRight(10) + $".DATA   {(v == null ? "0" : Number(v.Value))}", Line = line, Kind = "data" });
        }

        private void AppendHeapData(List<SharpSymbol> globalList)
        {
            var roots = globalList.Where(g => IsRef(g.Type) && !g.Field.Runtime).Select(g => g.Label).Append("0");
            Comment("Static fields the garbage collector starts from", 0);
            output.Add(new SharpOutLine { Text = "__roots:".PadRight(10) + $".DATA   {string.Join(", ", roots)}", Line = 0, Kind = "data" });
            Comment("Objects and arrays made with new are placed from here on, the stack grows down from the top", 0);
            output.Add(new SharpOutLine { Text = "__heap_start:", Line = 0, Kind = "label", Name = "__heap_start" });
        }

        private List<SharpOutLine> Descriptors()
        {
            var lines = new List<SharpOutLine>();
            foreach (var name in instantiated) lines.AddRange(Descriptor(classes.Get(name), name));
            if (lines.Count > 0)
            {
                lines.InsertRange(0, new[]
                {
                    new SharpOutLine { Text = "", Line = 0, Kind = "blank" },
                    new SharpOutLine { Text = "; Class layouts for the garbage collector: how many fields hold references, then where", Line = 0, Kind = "comment" },
                });
            }
            return lines;
        }

        private static IEnumerable<SharpOutLine> Descriptor(SharpClassInfo info, string name)
        {
            var refs = info.RefOffsets.Count > 0 ? string.Join(", ", info.RefOffsets.Select(o => "+" + Number(o))) : "none";
            yield return new SharpOutLine { Text = $"; {name}: {Number(info.Size)} cell{(info.Size == 1 ? "" : "s")}, references at {refs}", Line = 0, Kind = "comment" };
            var values = new[] { info.RefOffsets.Count }.Concat(info.RefOffsets).Select(Number);
            yield return new SharpOutLine { Text = $"__d_{name}:".PadRight(10) + $".DATA   {string.Join(", ", values)}", Line = 0, Kind = "data" };
        }

        private void GenHeapRuntime()
        {
            var heap = new SharpParser(SharpLexer.Lex(SharpRuntime.Heap), true).Program();
            var userSource = src;
            src = SharpRuntime.Heap.Split('\n');
            int start = output.Count;
            foreach (var f in heap.Fields)
            {
                f.Runtime = true;
                globals.Set(f.Name, new SharpSymbol { Kind = "global", Label = f.Name, Type = f.Type.Name, Field = f });
            }
            foreach (var m in heap.Methods) functions.Set(m.Name, m);
            Blank();
            Comment("The heap: allocation and a mark-and-sweep garbage collector, compiled from C#", 0);
            foreach (var m in heap.Methods) GenFunction(m);
            for (int i = start; i < output.Count; i++) HideRuntimeLine(output[i]);
            src = userSource;
        }

        private static void HideRuntimeLine(SharpOutLine x)
        {
            x.Line = 0;
            if (x.Kind == "comment") x.Text = Regex.Replace(x.Text, @"^; \d+: ", "; ");
        }

        private int? GlobalInitValue(SharpField f)
        {
            if (f.Init == null) return 0;
            return ConstValue(f.Init);
        }

        private void GenInit()
        {
            var context = new SharpFunctionContext { Name = "__init", Ret = "void" };
            fn = context;
            scope = null;
            PushScope();
            Blank();
            Comment("Static fields that need code to set them up", 0);
            Label("__init");
            int enterIndex = output.Count;
            Emit("ENTER", "0");
            foreach (var f in unit.Fields.Where(NeedsInitCode))
            {
                At(f);
                GenValue(f.Init, f.Type.Name, f);
                Emit("ST", f.Name);
            }
            SetEnter(enterIndex, context.Slots);
            Emit("RET", "2");
            PopScope();
        }

        private void SetEnter(int index, int slots)
        {
            output[index].Text = $"        {"ENTER".PadRight(6)} {Number(slots)}";
            output[index].Operand = Number(slots);
        }

        private void GenFunction(SharpMethod f)
        {
            var label = f.Label ?? f.Name;
            var context = new SharpFunctionContext { Name = f.Name, Ret = f.Ret.Name, RetLabel = $"__ret_{label}", TopLevel = f.TopLevel, Cls = f.Cls, Owner = f.Owner };
            fn = context;
            scope = null;
            PushScope();
            var parameters = f.Cls != null ? new[] { new SharpParam { Type = new SharpTypeRef { Name = f.Cls }, Name = "this", Line = f.Line, Col = f.Col } }.Concat(f.Params).ToList() : f.Params;
            DeclareParameters(parameters);
            Blank();
            curLine = f.Line;
            lastCommentLine = 0;
            FunctionHeading(f);
            int k = parameters.Count;
            var frame = string.Join(", ", parameters.Select((p, j) => $"{p.Name} FP+{Number(2 + (k - 1 - j))}"));
            Label(label);
            int enterIndex = output.Count;
            Emit("ENTER", "0");
            if (f.IsCtor) GenFieldInitialisers(f.Initialised);
            GenBlockBody(f.Body.Statements);
            curLine = f.Body.EndLine != 0 ? f.Body.EndLine : curLine;
            Label(context.RetLabel);
            if (f.IsCtor) Emit("LDL", Number(2 + k - 1));
            Emit("RET", Number(2 + k));
            SetEnter(enterIndex, context.Slots);
            AnnotateEnter(enterIndex, frame, context.LocalNames);
            PopScope();
        }

        private void FunctionHeading(SharpMethod f)
        {
            if (f.TopLevel)
            {
                Comment("Top-level statements", f.Line);
                return;
            }
            Comment($"{f.Line}: {Regex.Replace(SourceLine(f.Line).Trim(), @"\s*\{$", "")}", f.Line);
            lastCommentLine = f.Line;
        }

        private void AnnotateEnter(int enterIndex, string frame, string localNames)
        {
            if (frame.Length == 0 && string.IsNullOrEmpty(localNames)) return;
            var notes = new[] { frame, localNames }.Where(n => !string.IsNullOrEmpty(n));
            output[enterIndex].Text = output[enterIndex].Text.PadRight(24) + $"; {string.Join(", ", notes)}";
        }

        private void DeclareParameters(List<SharpParam> parameters)
        {
            int k = parameters.Count;
            for (int j = 0; j < k; j++)
            {
                var p = parameters[j];
                if (scope.Vars.ContainsKey(p.Name)) throw Fail($"parameter '{p.Name}' is listed twice", p);
                if (p.Type.Name == "var" || p.Type.Name == "void") throw Fail($"a parameter can not be {p.Type.Name}", p);
                scope.Vars[p.Name] = new SharpSymbol { Kind = "local", Offset = 2 + (k - 1 - j), Type = p.Type.Name, Name = p.Name };
            }
        }

        private void GenFieldInitialisers(List<SharpField> fields)
        {
            foreach (var f in fields)
            {
                At(f);
                var target = new SharpNode { K = "member", Obj = new SharpNode { K = "this", Line = f.Line, Col = f.Col }, Name = f.Name, Line = f.Line, Col = f.Col };
                GenEffect(new SharpNode { K = "assign", Op = "=", Target = target, E = f.Init, Line = f.Line, Col = f.Col });
            }
        }

        private void GenBlockBody(List<SharpNode> statements)
        {
            foreach (var s in statements) GenStatement(s);
        }

        private void NoteLocal(SharpSymbol v)
        {
            var label = $"{v.Name} FP{Number(v.Offset)}";
            fn.LocalNames = fn.LocalNames != null ? $"{fn.LocalNames}, {label}" : label;
        }
    }
}
