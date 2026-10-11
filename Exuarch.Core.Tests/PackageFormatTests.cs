using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Exuarch.Core;

namespace Exuarch.Core.Tests;

public class PackageFormatTests
{
    private static readonly string NewerVersion = $"\"formatVersion\": {FileFormat.Current + 1}";
    private static readonly string CurrentVersion = $"\"formatVersion\": {FileFormat.Current}";

    private static string FormatFolder(int version, [CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "PackageFormats", version.ToString());

    private static int VersionCount(string json) => Regex.Matches(json, "\"formatVersion\"").Count;

    public static TheoryData<int, string> FrozenPackages()
    {
        var data = new TheoryData<int, string>();
        for (int version = 1; version <= FileFormat.Current; version++)
            foreach (var file in Directory.GetFiles(FormatFolder(version), "*.json").Order())
                data.Add(version, Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(FrozenPackages))]
    public void EveryFrozenPackageOfEveryFormatStillBuildsAndAssembles(int version, string file)
    {
        var package = MachinePackage.FromJson(File.ReadAllText(Path.Combine(FormatFolder(version), file)));
        var machine = new Machine(package.Machine, "") { RecordHistory = false };
        foreach (var program in package.Programs)
        {
            var result = machine.Assembler.Analyze(program.Source ?? "");
            Assert.True(result.Success, $"{package.Name}, {program.Name}: {string.Join("; ", result.Diagnostics)}");
        }
    }

    [Fact]
    public void TheCurrentFormatHasFrozenPackages()
    {
        Assert.NotEmpty(Directory.GetFiles(FormatFolder(FileFormat.Current), "*.json"));
    }

    [Fact]
    public void AFileWithoutAFormatVersionIsFormatOne()
    {
        var package = File.ReadAllText(Path.Combine(FormatFolder(1), "tiny-16.json"));
        Assert.DoesNotContain("formatVersion", package);

        MachinePackage.FromJson(package);
        MachineDefinition.FromJson(BuiltInPackages.Default.Machine.ToJson());
        MicrocodeDefinition.FromJson(BuiltInPackages.Default.Machine.Decoder.Microcode.ToJson());
    }

    [Fact]
    public void APackageExportWritesTheVersionOnceAtTheTop()
    {
        var json = BuiltInPackages.Default.ToJson();

        Assert.StartsWith("{\n  " + CurrentVersion + ",", json);
        Assert.Equal(1, VersionCount(json));
    }

    [Fact]
    public void MachineAndMicrocodeFilesWriteTheVersionAtTheTop()
    {
        var machine = BuiltInPackages.Default.Machine;

        Assert.StartsWith("{\n  " + CurrentVersion + ",", machine.ToFileJson());
        Assert.StartsWith("{\n  " + CurrentVersion + ",", machine.Decoder.Microcode.ToFileJson());
        Assert.Equal(1, VersionCount(machine.ToFileJson()));
    }

    [Fact]
    public void AMachineFileReadsBackAsTheSameMachine()
    {
        var machine = BuiltInPackages.Default.Machine;

        Assert.Equal(machine.ToJson(), MachineDefinition.FromJson(machine.ToFileJson()).ToJson());
        Assert.Equal(machine.Decoder.Microcode.ToJson(), MicrocodeDefinition.FromJson(machine.Decoder.Microcode.ToFileJson()).ToJson());
    }

    [Fact]
    public void EditingAndUndoJsonCarriesNoVersion()
    {
        var machine = BuiltInPackages.Default.Machine;

        Assert.DoesNotContain("formatVersion", machine.ToJson());
        Assert.DoesNotContain("formatVersion", MachineDefinition.FromJson(machine.ToFileJson()).ToJson());
    }

    [Fact]
    public void AFileFromANewerFormatIsRejectedWithBothVersions()
    {
        var machine = BuiltInPackages.Default.Machine;
        var files = new System.Action[]
        {
            () => MachinePackage.FromJson(BuiltInPackages.Default.ToJson().Replace(CurrentVersion, NewerVersion)),
            () => MachineDefinition.FromJson(machine.ToFileJson().Replace(CurrentVersion, NewerVersion)),
            () => MicrocodeDefinition.FromJson(machine.Decoder.Microcode.ToFileJson().Replace(CurrentVersion, NewerVersion)),
        };
        foreach (var read in files)
        {
            var error = Assert.Throws<MachineDefinitionException>(read);
            Assert.Contains($"file format {FileFormat.Current + 1}", error.Message);
            Assert.Contains($"up to {FileFormat.Current}", error.Message);
        }
    }

    [Fact]
    public void ANewerFormatInsideAPackageIsRejected()
    {
        var package = BuiltInPackages.Default.ToJson();
        var machineStart = package.IndexOf("\"machine\": {") + "\"machine\": {".Length;
        var json = package.Insert(machineStart, $" {NewerVersion},");

        Assert.Throws<MachineDefinitionException>(() => MachinePackage.FromJson(json));
    }

    [Fact]
    public void AFormatVersionBelowOneIsRejected()
    {
        var json = BuiltInPackages.Default.ToJson().Replace(CurrentVersion, "\"formatVersion\": 0");

        Assert.Throws<MachineDefinitionException>(() => MachinePackage.FromJson(json));
    }

    [Fact]
    public void ASavedWorkspaceWithANewerPackageFormatIsUnreadable()
    {
        var workspace = new Workspace();
        workspace.Packages.Add(new SavedPackage { Package = BuiltInPackages.Default });
        var json = Regex.Replace(workspace.ToJson(), "\"formatVersion\":\\s*\\d+", NewerVersion);
        Assert.Contains(NewerVersion, json);

        Assert.False(Workspace.TryFromJson(json, out var read));
        Assert.Empty(read.Packages);
    }
}
