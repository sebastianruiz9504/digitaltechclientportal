using System.Net.Http.Json;

namespace DigitalTechClientPortal.Services
{
    public sealed class SecurityDataService
    {
        private readonly GraphClientFactory _factory;

        public SecurityDataService(GraphClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<SecureScoreDto?> GetSecureScoreAsync()
        {
            using var http = await _factory.CreateClientAsync();
            var resp = await http.GetFromJsonAsync<SecureScoreResponse>(
                "https://graph.microsoft.com/v1.0/security/secureScores?$top=1");
            return resp?.Value?.FirstOrDefault();
        }

        
    }

    public sealed class SecureScoreResponse { public List<SecureScoreDto>? Value { get; set; } }
    public sealed class SecureScoreDto
    {
        public double? CurrentScore { get; set; }
        public double? MaxScore { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
    }

    public sealed class RiskyUsersResponse { public RiskyUserDto[]? Value { get; set; } }
    public sealed class RiskyUserDto
    {
        public string? UserDisplayName { get; set; }
        public string? UserPrincipalName { get; set; }
        public string? RiskLevel { get; set; }
        public string? RiskState { get; set; }
    }
}