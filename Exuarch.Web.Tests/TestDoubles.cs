using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exuarch.Web.Components;

namespace Exuarch.Web.Tests;

internal sealed class MemoryStore : IBrowserStore
{
    public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
    public int Sets;
    public bool Reset;
    public bool PersistenceRequested;

    public Task<string> Get(string key) => Task.FromResult(Values.GetValueOrDefault(key));
    public Task<bool> Set(string key, string value)
    {
        Sets++;
        Values[key] = value;
        return Task.FromResult(true);
    }
    public Task Keep(string key, string value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }
    public Task ResetAll()
    {
        Reset = true;
        return Task.CompletedTask;
    }
    public Task RequestPersistentStorage()
    {
        PersistenceRequested = true;
        return Task.CompletedTask;
    }
}

internal sealed class MemoryRepo : IGitHubRepo
{
    public string Connected;
    public string Failure;
    public readonly Dictionary<string, string> Files = new Dictionary<string, string>();

    public Task<string> Repo() => Task.FromResult(Connected);
    public Task<string> Connect(string repo, string token)
    {
        if (token != "good") return Task.FromResult("bad token");
        Connected = repo;
        return Task.FromResult<string>(null);
    }
    public Task Disconnect()
    {
        Connected = null;
        return Task.CompletedTask;
    }
    public Task<string> Write(string path, string content, string message)
    {
        if (Failure != null) return Task.FromResult(Failure);
        Files[path] = content;
        return Task.FromResult<string>(null);
    }
    public Task<GitHubFolder> List(string folder)
    {
        return Task.FromResult(new GitHubFolder { Paths = Files.Keys.Where(p => p.StartsWith(folder + "/")).ToList() });
    }
    public Task<GitHubFile> Read(string path)
    {
        return Task.FromResult(Files.TryGetValue(path, out var text) ? new GitHubFile { Text = text } : new GitHubFile { Missing = true });
    }
}
