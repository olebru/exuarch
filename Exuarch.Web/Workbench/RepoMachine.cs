using System.Collections.Generic;
using Exuarch.Core;

namespace Exuarch.Web.Workbench
{
    public enum RepoMachineState
    {
        Same,
        OnlyOnGitHub,
        Differs,
        Unreadable,
    }

    public sealed record RepoMachine(string Name, string Path, MachinePackage Package, RepoMachineState State, string Error = null)
    {
        public static RepoMachine Unreadable(string path, string error) => new RepoMachine(System.IO.Path.GetFileNameWithoutExtension(path), path, null, RepoMachineState.Unreadable, error);
    }

    public sealed record RepoMachines(string Error, IReadOnlyList<RepoMachine> Machines);
}
