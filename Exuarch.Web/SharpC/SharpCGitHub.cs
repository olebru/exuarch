using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Core.SharpC;
using Exuarch.Web.Components;

namespace Exuarch.Web.SharpC
{
    public enum SharpMachineState { Unknown, Missing, Present, Unreadable }

    public sealed class SharpCGitHub
    {
        public const string MachinePath = "machines/SHARP-16.json";

        private readonly IGitHubRepo repo;
        private readonly SemaphoreSlim queue = new SemaphoreSlim(1, 1);

        public SharpCGitHub(IGitHubRepo repo)
        {
            this.repo = repo;
        }

        public string Connected { get; private set; }
        public string Failure { get; set; }

        public static string ProgramPath(string name) => $"sharpc/{GitHubRepo.FileName(name)}.cs";

        public async Task Restore()
        {
            Connected = await repo.Repo();
        }

        public async Task<string> Connect(string repository, string token, IReadOnlyList<SharpCProgram> saved)
        {
            var error = await repo.Connect(repository, token);
            if (error != null) return error;
            Connected = await repo.Repo();
            await Queued(async () =>
            {
                foreach (var program in saved) if (await WriteProgram(program) != null) return;
                await AddToMachine(saved.Select(p => SharpCWorkspace.PackageProgram(p.Name, p.Text)).Where(p => p != null).ToList(), "Save the SharpC programs into SHARP-16");
            });
            return null;
        }

        public async Task Disconnect()
        {
            await repo.Disconnect();
            Connected = null;
        }

        public Task Save(SharpCProgram program)
        {
            if (Connected == null) return Task.CompletedTask;
            return Queued(async () =>
            {
                if (await WriteProgram(program) != null) return;
                var entry = SharpCWorkspace.PackageProgram(program.Name, program.Text);
                if (entry == null)
                {
                    Failure = $"{program.Name} does not compile, so SHARP-16 on GitHub keeps its last version of it.";
                    return;
                }
                await AddToMachine(new[] { entry }, $"Save {program.Name} into SHARP-16 from SharpC");
            });
        }

        public async Task<SharpMachineState> MachineState()
        {
            var file = await repo.Read(MachinePath);
            if (file.Error != null) return SharpMachineState.Unreadable;
            return file.Missing ? SharpMachineState.Missing : SharpMachineState.Present;
        }

        public async Task<string> AddMachine(MachinePackage package)
        {
            string error = null;
            await Queued(async () => error = await repo.Write(MachinePath, package.ToJson(), "Add SHARP-16 from SharpC"));
            return error;
        }

        private async Task<string> WriteProgram(SharpCProgram program)
        {
            var error = await repo.Write(ProgramPath(program.Name), program.Text, $"Save {program.Name} from SharpC");
            if (error != null) Failure = $"{program.Name} is saved in this browser, but not on GitHub: {error}";
            return error;
        }

        private async Task AddToMachine(IReadOnlyList<SharpPackageProgram> programs, string message)
        {
            if (programs.Count == 0) return;
            var file = await repo.Read(MachinePath);
            if (file.Missing) return;
            if (file.Error != null) { Failure = $"The programs are in sharpc/ on GitHub, but not in {MachinePath}: {file.Error}"; return; }
            MachinePackage package;
            try { package = MachinePackage.FromJson(file.Text); }
            catch (MachineDefinitionException) { Failure = $"{MachinePath} in the repository is not a package file, so the programs were not added to it."; return; }
            foreach (var program in programs) Upsert(package, program);
            var error = await repo.Write(MachinePath, package.ToJson(), message);
            if (error != null) Failure = $"The programs are in sharpc/ on GitHub, but not in {MachinePath}: {error}";
        }

        private static void Upsert(MachinePackage package, SharpPackageProgram program)
        {
            var entry = Sharp16.MakePackage(new[] { program }).Programs[0];
            int index = package.Programs.FindIndex(p => p.Name == program.Name);
            if (index >= 0) package.Programs[index] = entry;
            else package.Programs.Add(entry);
        }

        private async Task Queued(System.Func<Task> work)
        {
            await queue.WaitAsync();
            try { await work(); }
            finally { queue.Release(); }
        }
    }
}
