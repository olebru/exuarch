using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Exuarch.Core;
using Exuarch.Web.Workbench;

namespace Exuarch.Web.Drawer
{
    public enum CommunityState { NotLoaded, Loading, Loaded, Unpublished, Failed }

    public sealed class CommunityCatalog
    {
        public const string Repository = "https://github.com/olebru/exuarch-machines";
        public static readonly Uri IndexUrl = new Uri("https://raw.githubusercontent.com/olebru/exuarch-machines/main/index.json");

        private readonly HttpClient http;

        public CommunityCatalog(HttpClient http)
        {
            this.http = http;
        }

        public CommunityState State { get; private set; }
        public string Problem { get; private set; }
        public IReadOnlyList<CommunityMachine> Machines => index?.Machines ?? new List<CommunityMachine>();
        public event Action Changed;
        private CommunityIndex index;

        public async Task LoadAsync(bool again = false)
        {
            if (State == CommunityState.Loading || (State == CommunityState.Loaded && !again)) return;
            Set(CommunityState.Loading, null);
            try
            {
                using var response = await http.GetAsync(IndexUrl);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Set(CommunityState.Unpublished, null);
                    return;
                }
                response.EnsureSuccessStatusCode();
                index = CommunityIndex.FromJson(await Limited(response, CommunityIndex.MaxIndexLength));
                Set(CommunityState.Loaded, null);
            }
            catch (Exception e) when (e is HttpRequestException || e is MachineDefinitionException || e is TaskCanceledException)
            {
                Set(CommunityState.Failed, e.Message);
            }
        }

        public async Task<MachinePackage> FetchAsync(CommunityMachine machine)
        {
            var url = CommunityIndex.Resolve(IndexUrl, machine.File);
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var package = PackageFile.Read(await Limited(response, CommunityIndex.MaxPackageLength), machine.File);
            package.Tagline ??= machine.Tagline;
            package.Tags ??= machine.Tags;
            return package;
        }

        public static string ImageUrl(CommunityMachine machine)
        {
            return machine.Image == null ? null : CommunityIndex.Resolve(IndexUrl, machine.Image).ToString();
        }

        private static async Task<string> Limited(HttpResponseMessage response, int limit)
        {
            if (response.Content.Headers.ContentLength > limit) throw new MachineDefinitionException($"The file is larger than {limit / 1024} KB.");
            var text = await response.Content.ReadAsStringAsync();
            if (text.Length > limit) throw new MachineDefinitionException($"The file is larger than {limit / 1024} KB.");
            return text;
        }

        private void Set(CommunityState state, string problem)
        {
            State = state;
            Problem = problem;
            Changed?.Invoke();
        }
    }
}
