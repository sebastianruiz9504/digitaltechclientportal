using System.Text;
using System.Text.Json;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace DigitalTechClientPortal.Services
{
    public sealed class GraphPermissionService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public GraphPermissionService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Task<SecurityPermissionStatus> GetSecurityPermissionStatusAsync()
        {
            return GetPermissionStatusAsync(
                GraphPermissionRequirements.SecurityPanelScopes,
                GraphPermissionRequirements.SecurityScopeDescriptions);
        }

        public Task<SecurityPermissionStatus> GetGovernancePermissionStatusAsync()
        {
            return GetPermissionStatusAsync(
                GraphPermissionRequirements.GovernanceScopes,
                GraphPermissionRequirements.GovernanceScopeDescriptions);
        }

        private async Task<SecurityPermissionStatus> GetPermissionStatusAsync(
            IEnumerable<string> requiredScopes,
            IReadOnlyDictionary<string, string> descriptions)
        {
            var required = requiredScopes.ToList();
            var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var ctx = _httpContextAccessor.HttpContext;
            if (ctx != null)
            {
                var accessToken = await ctx.GetTokenAsync("access_token");
                foreach (var scope in ReadScopes(accessToken))
                    granted.Add(scope);
            }

            var missing = required
                .Where(scope => !granted.Contains(scope))
                .ToList();

            return new SecurityPermissionStatus
            {
                RequiredScopes = required,
                GrantedScopes = granted.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(),
                MissingScopes = missing,
                ScopeDescriptions = new Dictionary<string, string>(
                    descriptions,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        private static IEnumerable<string> ReadScopes(string? accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return Array.Empty<string>();

            var parts = accessToken.Split('.');
            if (parts.Length < 2)
                return Array.Empty<string>();

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("scp", out var scp) ||
                    scp.ValueKind != JsonValueKind.String)
                {
                    return Array.Empty<string>();
                }

                return (scp.GetString() ?? "")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            catch
            {
                return Array.Empty<string>();
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
