using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace DigitalTechClientPortal.Services
{
    public sealed class GraphAppOnlyClientFactory
    {
        private readonly IConfidentialClientApplication _cca;
        private static readonly string[] Scopes = new[] { "https://graph.microsoft.com/.default" };

        public GraphAppOnlyClientFactory(IConfidentialClientApplication cca)
        {
            _cca = cca;
        }

        public async Task<HttpClient> CreateClientAsync()
        {
            var result = await _cca.AcquireTokenForClient(Scopes).ExecuteAsync();
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
            return http;
        }
    }
}