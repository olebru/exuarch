using System;
using System.Collections.Generic;

namespace Exuarch.Core.SharpC
{
    internal class SharpPos
    {
        public int Line { get; set; }
        public int Col { get; set; }
    }

    internal sealed class SharpCompileException : Exception
    {
        public SharpCompileException(string message, int line, int col) : base(message)
        {
            Line = line;
            Col = col;
        }

        public int Line { get; }
        public int Col { get; }

        public static SharpCompileException At(string message, SharpPos node)
        {
            return node == null ? new SharpCompileException(message, 0, 0) : new SharpCompileException(message, node.Line, node.Col);
        }
    }

    internal sealed class SharpToken : SharpPos
    {
        public string Kind { get; init; }
        public string Text { get; init; }
        public int Number { get; init; }
        public string Raw { get; init; }
        public List<SharpInterpolationPiece> Pieces { get; init; }

        public bool IsWord => Kind == "op" || Kind == "kw";

        public string Describe()
        {
            return Kind switch
            {
                "eof" => "the end of the file",
                "str" => "a string",
                "num" => $"'{Raw}'",
                "char" => "a character",
                _ => $"'{Text ?? "undefined"}'",
            };
        }
    }

    internal sealed class SharpInterpolationPiece : SharpPos
    {
        public string Text { get; init; }
        public string Source { get; init; }
    }

    internal sealed class SharpTypeRef : SharpPos
    {
        public string Name { get; set; }
    }

    internal sealed class SharpDecl : SharpPos
    {
        public string Name { get; init; }
        public SharpNode Init { get; init; }
    }

    internal sealed class SharpCaseLabel : SharpPos
    {
        public bool IsDefault { get; init; }
        public SharpNode E { get; init; }
    }

    internal sealed class SharpSwitchSection
    {
        public List<SharpCaseLabel> Labels { get; init; }
        public List<SharpNode> Body { get; init; }
    }

    internal sealed class SharpNode : SharpPos
    {
        public string K { get; init; }
        public int Value { get; init; }
        public string Text { get; init; }
        public string Name { get; init; }
        public string Op { get; init; }
        public SharpNode L { get; init; }
        public SharpNode R { get; init; }
        public SharpNode E { get; init; }
        public SharpNode Target { get; init; }
        public SharpNode C { get; init; }
        public SharpNode A { get; init; }
        public SharpNode B { get; init; }
        public SharpNode Body { get; init; }
        public SharpNode Else { get; init; }
        public SharpNode Init { get; init; }
        public SharpNode Callee { get; init; }
        public SharpNode Obj { get; init; }
        public SharpNode Index { get; init; }
        public SharpNode Size { get; init; }
        public SharpNode Collection { get; init; }
        public List<SharpNode> Statements { get; init; }
        public List<SharpNode> Args { get; init; }
        public List<SharpNode> Items { get; init; }
        public List<SharpNode> Update { get; init; }
        public List<SharpNode> Exprs { get; init; }
        public List<object> Parts { get; init; }
        public SharpTypeRef Type { get; init; }
        public string Cls { get; init; }
        public string Elem { get; init; }
        public List<SharpDecl> Decls { get; init; }
        public bool IsConst { get; init; }
        public List<SharpSwitchSection> Sections { get; init; }
        public int EndLine { get; init; }
    }

    internal sealed class SharpParam : SharpPos
    {
        public SharpTypeRef Type { get; init; }
        public string Name { get; init; }
    }

    internal sealed class SharpField : SharpPos
    {
        public SharpTypeRef Type { get; init; }
        public string Name { get; init; }
        public SharpNode Init { get; init; }
        public bool IsConst { get; init; }
        public string Owner { get; init; }
        public bool Runtime { get; set; }
    }

    internal sealed class SharpMethod : SharpPos
    {
        public string Name { get; set; }
        public SharpTypeRef Ret { get; init; }
        public List<SharpParam> Params { get; init; }
        public SharpNode Body { get; init; }
        public string Owner { get; set; }
        public string Cls { get; set; }
        public string Label { get; set; }
        public bool IsCtor { get; set; }
        public List<SharpField> Initialised { get; set; }
        public bool TopLevel { get; init; }
        public bool Synthetic { get; init; }
    }

    internal sealed class SharpClass : SharpPos
    {
        public string Name { get; init; }
        public List<SharpField> Fields { get; } = new List<SharpField>();
        public List<SharpMethod> Methods { get; } = new List<SharpMethod>();
        public SharpMethod Ctor { get; set; }
    }

    internal sealed class SharpUnit
    {
        public List<SharpMethod> Methods { get; } = new List<SharpMethod>();
        public List<SharpField> Fields { get; } = new List<SharpField>();
        public List<SharpNode> Statements { get; } = new List<SharpNode>();
        public List<SharpClass> Classes { get; } = new List<SharpClass>();
    }
}
