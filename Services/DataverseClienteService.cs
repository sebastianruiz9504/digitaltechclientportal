using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;


namespace DigitalTechClientPortal.Services
{
    public class DataverseClienteService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        private string _accessToken = string.Empty;
        private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

        public DataverseClienteService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        private async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                return _accessToken;

            var url = _config["Dataverse:Url"];
            var clientId = _config["Dataverse:ClientId"];
            var clientSecret = _config["Dataverse:ClientSecret"];
            var tenantId = _config["Dataverse:TenantId"];

            var authority = $"https://login.microsoftonline.com/{tenantId}";
            var scope = $"{url}/.default";

            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority(new Uri(authority))
                .Build();

            var result = await app.AcquireTokenForClient(new[] { scope }).ExecuteAsync();
            _accessToken = result.AccessToken;
            _tokenExpiresAt = result.ExpiresOn;
            return _accessToken;
        }
public class ClienteInfo
{
    public Guid Id { get; set; }
    public string? Nombre { get; set; }
}

public async Task<ClienteInfo?> GetClienteByEmailAsync(string email)
{
    if (string.IsNullOrWhiteSpace(email))
        return null;

    var token = await GetAccessTokenAsync();
    var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/');
    var query = $"cr07a_clientes?$select=cr07a_clienteid,cr07a_nombre&$filter=cr07a_correoelectronico eq '{email.Replace("'", "''")}'";

    var url = $"{baseUrl}/api/data/v9.2/{query}";

    var client = _httpClientFactory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
    client.DefaultRequestHeaders.Add("OData-Version", "4.0");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    var resp = await client.GetAsync(url);
    if (!resp.IsSuccessStatusCode)
    {
        var body = await resp.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Dataverse GET {resp.StatusCode}. URL: {url}. Body: {body}");
    }

    var json = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(json);
    var values = doc.RootElement.GetProperty("value");
    if (values.GetArrayLength() > 0)
    {
        return new ClienteInfo
        {
            Id = values[0].GetProperty("cr07a_clienteid").GetGuid(),
            Nombre = values[0].GetProperty("cr07a_nombre").GetString()
        };
    }

    return null;
}
        /// <summary>
        /// Busca el cliente en Dataverse basado en el email del usuario.
        /// </summary>
        public async Task<string?> GetClienteNombreByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            var token = await GetAccessTokenAsync();
            var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/');
            // Ajusta el nombre de columna del email en cr07a_cliente si es distinto.
            var query = $"cr07a_clientes?$select=cr07a_nombre&$filter=cr07a_correoelectronico eq '{email.Replace("'", "''")}'";

            var url = $"{baseUrl}/api/data/v9.2/{query}";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Dataverse GET {resp.StatusCode}. URL: {url}. Body: {body}");
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var values = doc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                var nombre = values[0].GetProperty("cr07a_nombre").GetString();
                return nombre;
            }

            return null;
        }
    }
}