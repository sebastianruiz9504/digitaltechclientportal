using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace DigitalTechClientPortal.Services
{
    public sealed class GraphClientFactory
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GraphClientFactory(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<HttpClient> CreateClientAsync()
        {
            var ctx = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
            var token = await ctx.GetTokenAsync("access_token");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Sin access_token. Verifica SaveTokens y scopes.");

            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }
    }
}