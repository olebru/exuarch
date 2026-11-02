using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Exuarch.Web.Components
{
    public interface IGitHubRepo
    {
        Task<string> Repo();
        Task<string> Connect(string repo, string token);
        Task Disconnect();
        Task<string> Write(string path, string content, string message);
        Task<GitHubFolder> List(string folder);
        Task<GitHubFile> Read(string path);
    }

    public sealed class GitHubFolder
    {
        public List<string> Paths { get; set; } = new List<string>();
        public string Error { get; set; }
    }

    public sealed class GitHubFile
    {
        public string Text { get; set; }
        public bool Missing { get; set; }
        public string Error { get; set; }
    }

    public sealed class GitHubRepo : IGitHubRepo
    {
        private const string InvalidFileNameCharacters = "\\/:*?\"<>|";
        private readonly IJSRuntime js;

        public GitHubRepo(IJSRuntime js)
        {
            this.js = js;
        }

        public static string MachinePath(string name)
        {
            var file = new string((name ?? "").Select(c => char.IsControl(c) || InvalidFileNameCharacters.Contains(c) ? '-' : c).ToArray()).Trim();
            return $"machines/{(file.Length == 0 ? "unnamed" : file)}.json";
        }

        public Task<string> Repo() => js.InvokeAsync<string>("exuarchGitHub.repo").AsTask();
        public Task<string> Connect(string repo, string token) => js.InvokeAsync<string>("exuarchGitHub.connect", repo, token).AsTask();
        public Task Disconnect() => js.InvokeVoidAsync("exuarchGitHub.disconnect").AsTask();
        public Task<string> Write(string path, string content, string message) => js.InvokeAsync<string>("exuarchGitHub.write", path, content, message).AsTask();
        public Task<GitHubFolder> List(string folder) => js.InvokeAsync<GitHubFolder>("exuarchGitHub.list", folder).AsTask();
        public Task<GitHubFile> Read(string path) => js.InvokeAsync<GitHubFile>("exuarchGitHub.read", path).AsTask();
    }
}
