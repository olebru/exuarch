using System.Collections.Generic;
using System.Linq;

namespace Exuarch.Core.SharpC
{
    internal sealed record SharpTarget(SharpClassInfo Static, SharpClassInfo Instance);

    internal sealed partial class SharpGenerator
    {
        private static bool IsArray(string t) => t != null && t.EndsWith("[]");

        private static bool IsInt(string t) => t == "int" || t == "char";

        private static string ElementOf(string arrayType) => arrayType.Substring(0, arrayType.Length - 2);

        private static string An(string t) => (t.Length > 0 && "aeiou".Contains(t[0]) ? "an " : "a ") + t;

        private void DeclareTopLevel()
        {
            foreach (var f in unit.Fields) DeclareStaticField(f);
            foreach (var m in unit.Methods) DeclareFunction(m);
            foreach (var c in unit.Classes) classes.Set(c.Name, null);
            foreach (var c in unit.Classes) DeclareClass(c);
            if (unit.Statements.Count > 0) DeclareTopLevelStatements();
            if (!functions.Has("Main")) throw new SharpCompileException("there is no Main method and no top-level statements: nothing to run", 1, 1);
        }

        private void DeclareStaticField(SharpField f)
        {
            if (globals.Has(f.Name)) throw Fail($"'{f.Name}' is defined more than once", f);
            if (f.IsConst)
            {
                var v = ConstValue(f.Init);
                if (v == null) throw Fail($"the value of const '{f.Name}' must be a constant", (SharpPos)f.Init ?? f);
                globals.Set(f.Name, new SharpSymbol { Kind = "const", Value = v.Value, Type = f.Type.Name == "var" ? "int" : f.Type.Name });
                return;
            }
            if (f.Type.Name == "var") throw Fail("'var' can not be used for a field", f);
            globals.Set(f.Name, new SharpSymbol { Kind = "global", Label = f.Name, Type = f.Type.Name, Field = f });
        }

        private void DeclareFunction(SharpMethod m)
        {
            if (functions.Has(m.Name) || globals.Has(m.Name)) throw Fail($"'{m.Name}' is defined more than once (overloads are not supported)", m);
            if (m.Ret.Name == "var") throw Fail("a method can not return var", m.Ret);
            functions.Set(m.Name, m);
        }

        private void DeclareTopLevelStatements()
        {
            if (functions.Has("Main")) throw Fail("a program with top-level statements can not also have a Main method", functions.Get("Main"));
            var first = unit.Statements[0];
            var body = new SharpNode { K = "block", Statements = unit.Statements, Line = first.Line };
            functions.Set("Main", new SharpMethod { Name = "Main", Ret = new SharpTypeRef { Name = "void" }, Params = new List<SharpParam>(), Body = body, Line = first.Line, Col = first.Col, TopLevel = true });
        }

        private void DeclareClass(SharpClass c)
        {
            var info = new SharpClassInfo { Name = c.Name };
            for (int i = 0; i < c.Fields.Count; i++) DeclareInstanceField(info, c, c.Fields[i], i);
            foreach (var m in c.Methods) DeclareMethod(info, c, m);
            var initialised = c.Fields.Where(f => f.Init != null).ToList();
            if (c.Ctor != null || initialised.Count > 0) info.Ctor = Constructor(c, initialised);
            info.Size = System.Math.Max(1, c.Fields.Count);
            classes.Set(c.Name, info);
        }

        private void DeclareInstanceField(SharpClassInfo info, SharpClass c, SharpField f, int i)
        {
            if (info.Fields.ContainsKey(f.Name)) throw Fail($"'{f.Name}' is defined more than once in {c.Name}", f);
            if (f.Type.Name == "var") throw Fail("'var' can not be used for a field", f);
            if (f.Type.Name == "void") throw Fail("a field can not be void", f);
            info.Fields[f.Name] = new SharpSymbol { Kind = "field", Offset = i, Type = f.Type.Name, Cls = c.Name, Name = f.Name, Field = f };
            if (IsRef(f.Type.Name)) info.RefOffsets.Add(i);
        }

        private void DeclareMethod(SharpClassInfo info, SharpClass c, SharpMethod m)
        {
            if (info.Methods.Has(m.Name) || info.Fields.ContainsKey(m.Name)) throw Fail($"'{m.Name}' is defined more than once in {c.Name} (overloads are not supported)", m);
            if (m.Ret.Name == "var") throw Fail("a method can not return var", m.Ret);
            m.Cls = c.Name;
            m.Label = $"__m_{c.Name}_{m.Name}";
            info.Methods.Set(m.Name, m);
        }

        private static SharpMethod Constructor(SharpClass c, List<SharpField> initialised)
        {
            int line = c.Ctor != null ? c.Ctor.Line : c.Line;
            var ctor = c.Ctor ?? new SharpMethod
            {
                Name = c.Name, Ret = new SharpTypeRef { Name = "void" }, Params = new List<SharpParam>(),
                Body = new SharpNode { K = "block", Statements = new List<SharpNode>(), Line = line }, Line = line, Col = c.Col, Synthetic = true,
            };
            ctor.Cls = c.Name;
            ctor.IsCtor = true;
            ctor.Label = $"__c_{c.Name}";
            ctor.Name = c.Name;
            ctor.Initialised = initialised;
            return ctor;
        }

        private bool IsRef(string t) => IsArray(t) || classes.Has(t);

        private bool IsClassName(string name)
        {
            if (!classes.Has(name)) return false;
            if (InScope(name)) return false;
            return InstanceField(name) == null && !globals.Has(name);
        }

        private SharpSymbol InstanceField(string name)
        {
            var cls = fn?.Cls != null ? classes.Get(fn.Cls) : null;
            return cls != null && cls.Fields.TryGetValue(name, out var f) ? f : null;
        }

        private static SharpNode FieldNode(SharpNode e)
        {
            return new SharpNode { K = "member", Obj = new SharpNode { K = "this", Line = e.Line, Col = e.Col }, Name = e.Name, Line = e.Line, Col = e.Col };
        }

        private SharpTarget MemberTarget(SharpNode obj)
        {
            if (obj.K == "name" && IsClassName(obj.Name)) return new SharpTarget(classes.Get(obj.Name), null);
            if (obj.K == "name" && !IsVariable(obj.Name)) return null;
            var t = TypeOf(obj);
            return classes.Has(t) ? new SharpTarget(null, classes.Get(t)) : null;
        }

        private SharpSymbol StaticMember(SharpClassInfo info, string name, SharpPos node)
        {
            var g = globals.Get(name);
            if (g?.Field != null && g.Field.Owner == info.Name) return g;
            var f = functions.Get(name);
            if (f != null && f.Owner == info.Name) return SharpSymbol.Of(f);
            if (info.Fields.ContainsKey(name) || info.Methods.Has(name)) throw Fail($"'{info.Name}.{name}' needs an object: it is not static", node);
            throw Fail($"{info.Name} has no static member '{name}'", node);
        }

        private SharpSymbol InstanceMember(SharpClassInfo info, string name, SharpPos node)
        {
            if (info.Fields.TryGetValue(name, out var field)) return field;
            if (info.Methods.Has(name)) return SharpSymbol.Of(info.Methods.Get(name));
            throw Fail($"{info.Name} has no field or method '{name}'", node);
        }

        private SharpSymbol Lookup(string name, SharpPos node)
        {
            var scoped = ScopedVariable(name);
            if (scoped != null) return scoped;
            var field = InstanceField(name);
            if (field != null) return field;
            if (globals.Has(name)) return globals.Get(name);
            if (functions.Has(name)) throw Fail($"'{name}' is a method: call it with {name}(…)", node);
            var owner = fn?.Owner != null ? classes.Get(fn.Owner) : null;
            if (owner != null && owner.Fields.ContainsKey(name)) throw Fail($"'{name}' is a field of each {owner.Name} object, and a static method has no object to read it from", node);
            throw Fail($"'{name}' is not defined", node);
        }

        private SharpSymbol DeclareLocal(string name, string type, SharpPos node)
        {
            if (InScope(name)) throw Fail($"'{name}' is already defined in this method", node);
            int slot = NewSlot();
            var v = new SharpSymbol { Kind = "local", Offset = -(1 + slot), Type = type, Name = name };
            scope.Vars[name] = v;
            return v;
        }

        private bool IsVariable(string name)
        {
            if (InScope(name)) return true;
            return InstanceField(name) != null || globals.Has(name);
        }
    }
}
