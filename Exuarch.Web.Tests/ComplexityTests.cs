using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Exuarch.Web.Tests;

// No method, accessor or constructor in the app may have a cyclomatic complexity above MaxComplexity. It is counted
// the McCabe way, 1 plus a decision point for every if, loop, catch, case label, switch expression arm (but the
// discard), ?:, ?., ??, ??=, && and ||, and every `and`/`or` in a pattern. A lambda or local function counts toward
// the member it is written in. The .razor files are measured on the C# they compile to, so @if and @foreach in markup
// count toward the component's BuildRenderTree, as do the lambdas of its event handlers.
public class ComplexityTests
{
    public const int MaxComplexity = 10;

    private static string Root([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), ".."));

    public record Member(string File, string Name, int Line, int Complexity);

    [Fact]
    public void NoMemberIsTooComplex()
    {
        var members = Measure().ToList();
        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.File.EndsWith(".razor") && m.Name.EndsWith(".BuildRenderTree"));
        var tooComplex = members.Where(m => m.Complexity > MaxComplexity).OrderByDescending(m => m.Complexity).ToList();
        Assert.True(tooComplex.Count == 0, $"{tooComplex.Count} members have a cyclomatic complexity above {MaxComplexity}:\n"
            + string.Join("\n", tooComplex.Select(m => $"  {m.Complexity,4}  {m.File}:{m.Line}  {m.Name}")));
    }

    [Fact]
    public void TheCountingRulesAreTheOnesDescribed()
    {
        const string source = """
            class C
            {
                int Simple() { return 1; }
                int Branches(int x, string s, int[] a)
                {
                    if (x > 0 && x < 9 || x == 20) x++;
                    for (int i = 0; i < 2; i++) x += a?.Length ?? 0;
                    foreach (var v in a) { while (v > 0) break; do { } while (false); }
                    try { } catch (System.Exception) { }
                    switch (x) { case 1: case 2: break; default: break; }
                    x = x switch { 1 => 2, > 5 and < 9 or 12 => 3, _ => 4 };
                    s ??= "";
                    System.Func<int, int> f = y => y > 0 ? y : -y;
                    return x;
                }
                System.Func<int, int> field = y => y > 0 && y < 3 ? y : 0;
                int Property { get; } = 1 > 0 ? 1 : 0;
            }
            """;
        var counted = Count(CSharpSyntaxTree.ParseText(source), "C.cs").ToDictionary(m => m.Name, m => m.Complexity);
        Assert.Equal(1, counted["C.Simple"]);
        // if, &&, ||, for, ?., ??, foreach, while, do, catch, two cases, two arms (the discard does not count), and, or,
        // ??= and ?: are eighteen decisions.
        Assert.Equal(1 + 18, counted["C.Branches"]);
        Assert.Equal(3, counted["C.field"]);
        Assert.Equal(2, counted["C.Property"]);
    }

    public static IEnumerable<Member> Measure()
    {
        var root = Root();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "Exuarch.Core"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Exuarch.Web"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Exuarch.Cli"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        foreach (var file in sources)
        {
            foreach (var member in Count(CSharpSyntaxTree.ParseText(File.ReadAllText(file)), Path.GetRelativePath(root, file))) yield return member;
        }
        foreach (var (razor, generated) in GeneratedRazor(root))
        {
            foreach (var member in Count(CSharpSyntaxTree.ParseText(File.ReadAllText(generated)), razor)) yield return member;
        }
    }

    // The C# generated for each .razor file that is still in the project, from the newest build.
    private static IEnumerable<(string Razor, string Generated)> GeneratedRazor(string root)
    {
        var web = Path.Combine(root, "Exuarch.Web");
        var folder = Path.Combine(web, "obj", "generated", "Microsoft.CodeAnalysis.Razor.Compiler", "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator");
        Assert.True(Directory.Exists(folder), $"Build Exuarch.Web first: there is no {folder}.");
        var razorFiles = Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(web, f))
            .Where(f => Path.GetFileName(f) != "_Imports.razor")
            .ToList();
        Assert.NotEmpty(razorFiles);
        foreach (var razor in razorFiles)
        {
            var generated = Path.Combine(folder, razor.Replace(".razor", "_razor.g.cs"));
            Assert.True(File.Exists(generated), $"No generated code for {razor} at {generated}. Rebuild Exuarch.Web.");
            Assert.True(File.GetLastWriteTimeUtc(generated) >= File.GetLastWriteTimeUtc(Path.Combine(web, razor)), $"The generated code for {razor} is older than it. Rebuild Exuarch.Web.");
            yield return (Path.Combine("Exuarch.Web", razor), generated);
        }
    }

    private static IEnumerable<Member> Count(SyntaxTree tree, string file)
    {
        foreach (var member in tree.GetRoot().DescendantNodes().Where(IsMember))
        {
            int complexity = 1 + member.DescendantNodes().Count(IsDecision) + member.DescendantTokens().Count(IsDecisionToken);
            var type = member.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "?";
            yield return new Member(file, $"{type}.{Name(member)}", member.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1, complexity);
        }
    }

    // Accessors with a body are counted on their own; a property with only an expression body is one member, and so
    // is a field or property initializer, which can hold a lambda.
    private static bool IsMember(SyntaxNode node)
    {
        return node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or FieldDeclarationSyntax
            || (node is PropertyDeclarationSyntax property && (property.ExpressionBody != null || property.Initializer != null))
            || (node is IndexerDeclarationSyntax indexer && indexer.ExpressionBody != null);
    }

    private static string Name(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Text + "()",
            AccessorDeclarationSyntax accessor => $"{Name(accessor.Parent.Parent)}.{accessor.Keyword.Text}",
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(v => v.Identifier.Text)),
            IndexerDeclarationSyntax => "this[]",
            EventDeclarationSyntax e => e.Identifier.Text,
            _ => node.Kind().ToString(),
        };
    }

    private static bool IsDecision(SyntaxNode node)
    {
        return node switch
        {
            IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                or CatchClauseSyntax or ConditionalExpressionSyntax or ConditionalAccessExpressionSyntax
                or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or BinaryPatternSyntax => true,
            SwitchExpressionArmSyntax arm => arm.Pattern is not DiscardPatternSyntax,
            _ => false,
        };
    }

    private static bool IsDecisionToken(SyntaxToken token)
    {
        return token.Kind() is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken
            or SyntaxKind.QuestionQuestionToken or SyntaxKind.QuestionQuestionEqualsToken;
    }
}
