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
    }
}
