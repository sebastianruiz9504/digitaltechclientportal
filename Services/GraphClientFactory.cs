using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

            var sessionTenantId = ctx.User.FindFirst("tid")?.Value
                ?? ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
            var tokenTenantId = ReadJwtClaim(token, "tid");
            if (!string.IsNullOrWhiteSpace(sessionTenantId) &&
                !string.IsNullOrWhiteSpace(tokenTenantId) &&
                !string.Equals(sessionTenantId, tokenTenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El token de Microsoft Graph no corresponde al tenant de la sesion actual. Cierra sesion e inicia nuevamente.");
            }

            var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        private static string? ReadJwtClaim(string token, string claimName)
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return null;

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(json);

                return doc.RootElement.TryGetProperty(claimName, out var claim) &&
                    claim.ValueKind == JsonValueKind.String
                    ? claim.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value
                .Replace('-', '+')
                .Replace('_', '/');

            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
    }
}
