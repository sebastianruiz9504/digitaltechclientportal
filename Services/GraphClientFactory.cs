using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace DigitalTechClientPortal.Services
{
    public sealed class GraphClientFactory
    {
        private readonly string _clientId;
        private readonly string _clientSecret;

        public GraphClientFactory(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        private async Task<string> GetTokenAsync(string? tenantId = null)
        {
            // Si no se pasa tenantId, usamos "common" (multi-tenant)
            var authority = string.IsNullOrEmpty(tenantId)
                ? "https://login.microsoftonline.com/common"
                : $"https://login.microsoftonline.com/{tenantId}";

            var app = ConfidentialClientApplicationBuilder
                .Create(_clientId)
                .WithClientSecret(_clientSecret)
                .WithAuthority(authority)
                .Build();

            var result = await app.AcquireTokenForClient(
                new[] { "https://graph.microsoft.com/.default" })
                .ExecuteAsync();

            return result.AccessToken;
        }

        public async Task<HttpClient> CreateClientAsync(string? tenantId = null)
        {
            var token = await GetTokenAsync(tenantId);
            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return http;
        }
    }
}