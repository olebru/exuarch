using System;
using System.IO;
using System.Linq;
using Exuarch.Core;

namespace Exuarch.Cli.Tests;

public class CliTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("exuarch-cli-");

    public void Dispose() { folder.Delete(true); }

    private static (int Code, string Output, string Error) Exuarch(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string File(string name, string contents)
    {
        var path = Path.Combine(folder.FullName, name);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    private string Package(Action<MachinePackage> change)
    {
        var package = BuiltInPackages.Get("BYOC-16");
        change(package);
        return File("package.json", package.ToJson());
    }

    [Fact]
    public void WithoutArgumentsItPrintsEveryCommand()
    {
        var (code, output, _) = Exuarch();
        Assert.Equal(ExitCode.Success, code);
        foreach (var command in Cli.Commands) Assert.Contains($"exuarch {command.Usage}", output);
    }

    [Fact]
    public void AnUnknownCommandIsAUsageError()
    {
        var (code, _, error) = Exuarch("frobnicate");
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("Unknown command 'frobnicate'", error);
    }

    [Fact]
    public void AnUnknownOptionNamesTheOptionsThereAre()
    {
        var (code, _, error) = Exuarch("run", "BYOC-16", "--tick", "5");
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("Unknown option --tick. Its options are --program, --file, --ticks, --memory.", error);
    }

    [Fact]
    public void ExamplesListsTheBuiltInPackagesWithNumberedPrograms()
    {
        var (code, output, _) = Exuarch("examples");
        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("BYOC-16 (simple): The original accumulator machine, with a stack", output);
        Assert.Contains("  1. Hello, world on the LCD", output);
    }

    [Fact]
    public void EveryExamplePackageValidates()
    {
        foreach (var package in BuiltInPackages.Examples)
        {
            var (code, output, _) = Exuarch("validate", package.Name);
            Assert.True(code == ExitCode.Success, $"{package.Name}:\n{output}");
            Assert.EndsWith("0 errors, 0 warnings." + Environment.NewLine, output);
        }
    }

    [Fact]
    public void ValidateReportsAnUnknownDeviceType()
    {
        var path = Package(p => p.Machine.Devices.First(d => d.Id == "rega").Type = "registr");
        var (code, output, _) = Exuarch("validate", path);
        Assert.Equal(ExitCode.Problems, code);
        Assert.Contains("error: ", output);
        Assert.Contains("registr", output);
    }

    [Fact]
    public void ValidateReportsAProgramThatDoesNotAssembleWithItsLine()
    {
        var path = Package(p => p.Programs[1].Source = "LAI 7\nFOO 3\nHLT");
        var (code, output, _) = Exuarch("validate", path);
        Assert.Equal(ExitCode.Problems, code);
        Assert.Contains("program 1 \"Hello, world on the LCD\": 42 words", output);
        Assert.Contains("error: program 2 \"Fibonacci on the LCD\": Line 2: unknown mnemonic 'FOO'", output);
        Assert.Contains("1 error, 0 warnings.", output);
    }

    [Fact]
    public void ValidateReportsJsonThatDoesNotParse()
    {
        var (code, output, _) = Exuarch("validate", File("broken.json", "{"));
        Assert.Equal(ExitCode.Problems, code);
        Assert.Contains("not valid JSON", output);
    }

    [Fact]
    public void APackageThatIsNeitherAFileNorBuiltInIsAUsageError()
    {
        var (code, _, error) = Exuarch("validate", "nope.json");
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("no built in package of that name", error);
    }

    [Fact]
    public void AssemblePrintsAddressesWordsAndSource()
    {
        var (code, output, _) = Exuarch("assemble", "BYOC-16", "--file", File("p.asm", "LAI 7\nHLT\n"));
        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("0000  0053 0007        LAI 7", output);
        Assert.Contains("p.asm: 3 words.", output);
    }

    [Fact]
    public void AssembleReportsEveryErrorAndNoListing()
    {
        var (code, output, _) = Exuarch("assemble", "BYOC-16", "--file", File("p.asm", "LAI 7\nFOO 3\nJMP nowhere\n"));
        Assert.Equal(ExitCode.Problems, code);
        Assert.Contains("error: Line 2: unknown mnemonic 'FOO'", output);
        Assert.Contains("error: Line 3: unknown label 'nowhere'", output);
        Assert.DoesNotContain("0000", output);
    }

    [Fact]
    public void AProgramIsChosenByNameOrNumber()
    {
        var byName = Exuarch("assemble", "BYOC-16", "--program", "fibonacci on the lcd").Output;
        var byNumber = Exuarch("assemble", "BYOC-16", "--program", "2").Output;
        Assert.Equal(byNumber, byName);
        var (code, _, error) = Exuarch("assemble", "BYOC-16", "--program", "9");
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("  4. Colour gradient on the screen", error);
    }

    [Fact]
    public void RunShowsTheDisplayAndTheRegistersOnceItHalts()
    {
        var (code, output, _) = Exuarch("run", "BYOC-16");
        Assert.Equal(ExitCode.Success, code);
        Assert.StartsWith("Halted after 388 ticks.", output);
        Assert.Contains("  |HELLO, WORLD!                   |", output);
        Assert.Contains("  rega        000D  13", output);
        Assert.Contains("fb (SCREEN): 0 of 307,200 pixels are not black (RGB565).", output);
    }

    [Fact]
    public void RunStopsAtTheTickLimitAndSaysWhereItWas()
    {
        var (code, output, _) = Exuarch("run", "BYOC-16", "--program", "4", "--ticks", "1_000");
        Assert.Equal(ExitCode.Success, code);
        Assert.StartsWith("Still running after 1,000 ticks. It was at ", output);
    }

    [Fact]
    public void RunDrawsAThumbnailOfAScreenThatHasAPicture()
    {
        var output = Exuarch("run", "BYOC-16", "--program", "Colour bands on the screen").Output;
        var thumbnail = output.Split(Environment.NewLine).SkipWhile(l => !l.StartsWith("fb (SCREEN)")).Skip(1).TakeWhile(l => l.StartsWith("  |")).ToList();
        Assert.Equal(30, thumbnail.Count);
        Assert.All(thumbnail, line => Assert.Equal(84, line.Length));
        Assert.True(thumbnail.Distinct().Count() > 1, "the bands should not all look the same");
    }

    [Fact]
    public void RunDumpsMemoryAfterTheProgram()
    {
        var program = File("p.asm", "LAI 7\nSTA 5\nHLT\n");
        var (code, output, _) = Exuarch("run", "BYOC-16", "--file", program, "--memory", "mmu/0:0:8", "--memory", "mem:0x0:3");
        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("mmu/0 0000-0007:" + Environment.NewLine + "  0000  0000 0000 0000 0000 0000 0007 0000 0000", output);
        Assert.Contains("mem 0000-0002:" + Environment.NewLine + "  0000  0053 0007 ", output);
    }

    [Theory]
    [InlineData("nope:0", "There is no device 'nope'. The devices are regi, pc,")]
    [InlineData("mem:0xFFFF:2", "mem holds 4,096 words, so mem:0xFFFF:2 reaches past its end.")]
    [InlineData("rega:0", "rega has no memory to show.")]
    [InlineData("mem", "--memory takes <device>:<start>[:<count>]")]
    [InlineData("mmu/9999:0", "mmu has banks 0 to")]
    public void ABadMemoryRangeIsAUsageError(string spec, string expected)
    {
        var (code, _, error) = Exuarch("run", "BYOC-16", "--memory", spec);
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void DocsListsTheGuidesAndTheDeviceTypes()
    {
        var (code, output, _) = Exuarch("docs");
        Assert.Equal(ExitCode.Success, code);
        Assert.Contains("  microcode ", output);
        Assert.Contains("  register ", output);
    }

    [Theory]
    [InlineData("microcode", "# ")]
    [InlineData("Register", "# register")]
    public void DocsPrintsAGuideOrADeviceReference(string page, string expected)
    {
        var (code, output, _) = Exuarch("docs", page);
        Assert.Equal(ExitCode.Success, code);
        Assert.StartsWith(expected, output);
    }

    [Fact]
    public void DocsSearchesTheHandbook()
    {
        var (code, output, _) = Exuarch("docs", "--search", "opcode field");
        Assert.Equal(ExitCode.Success, code);
        Assert.StartsWith("opcode-fields (Concepts): ", output);
        Assert.Equal(ExitCode.Usage, Exuarch("docs", "no-such-page").Code);
    }

    [Fact]
    public void ExportWritesAPackageThatValidatesAndNeverOverwrites()
    {
        var path = Path.Combine(folder.FullName, "mine.json");
        Assert.Equal(ExitCode.Success, Exuarch("export", "risc-16", path).Code);
        Assert.Equal("RISC-16", MachinePackage.FromJson(System.IO.File.ReadAllText(path)).Name);
        Assert.Equal(ExitCode.Success, Exuarch("validate", path).Code);
        var (code, _, error) = Exuarch("export", "RISC-16", path);
        Assert.Equal(ExitCode.Usage, code);
        Assert.Contains("already exists", error);
    }

    [Fact]
    public void VersionPrintsTheVersion()
    {
        var (code, output, _) = Exuarch("--version");
        Assert.Equal(ExitCode.Success, code);
        Assert.Equal(Cli.Version + Environment.NewLine, output);
    }
}
