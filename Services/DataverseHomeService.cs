using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public class DataverseHomeService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        private string _accessToken = string.Empty;
        private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

        public DataverseHomeService(IConfiguration config, IHttpClientFactory httpClientFactory)
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

        private async Task<JsonDocument> GetAsync(string odataQuery)
        {
            var token = await GetAccessTokenAsync();
            var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/');
            var url = $"{baseUrl}/api/data/v9.2/{odataQuery}";

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
            return JsonDocument.Parse(json);
        }

        // Cloud (Sales Performance) existente
        public async Task<List<ProductVm>> GetSalesPerformanceProductsAsync(Guid clienteId)
        {
            var products = new List<ProductVm>();

            var query =
                $"cr07a_salesperformancerecords?" +
                "$select=cr07a_productname,cr07a_quantity" +
                $"&$filter=_cr07a_clientelookup_value eq {clienteId}";

            var json = await GetAsync(query);

            foreach (var e in json.RootElement.GetProperty("value").EnumerateArray())
            {
                var name = e.TryGetProperty("cr07a_productname", out var pn) ? (pn.GetString() ?? "") : "";
                var qty = e.TryGetProperty("cr07a_quantity", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : 0;

                products.Add(new ProductVm
                {
                    ProductName = name,
                    Quantity = qty
                });
            }

            return products;
        }

        // Copiers: cr07a_productoscopiers
        public async Task<List<ProductVm>> GetCopiersProductsAsync(Guid clienteId)
        {
            var products = new List<ProductVm>();

            // Asume el mismo lookup a cliente que en SalesPerformance: _cr07a_clientelookup_value
            // Si tu entidad usa otro nombre de relación, cámbialo aquí.
            var query =
                $"cr07a_productoscopierses?" +
                "$select=cr07a_producto,cr07a_cantidad,cr07a_descripcion" +
                $"&$filter=_cr07a_cliente_value eq {clienteId}";


            var json = await GetAsync(query);

            foreach (var e in json.RootElement.GetProperty("value").EnumerateArray())
            {
                var name = e.TryGetProperty("cr07a_producto", out var pn) ? (pn.GetString() ?? "") : "";
                var qty = e.TryGetProperty("cr07a_cantidad", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : 0;
                var desc = e.TryGetProperty("cr07a_descripcion", out var d) ? (d.GetString() ?? "") : "";

                products.Add(new ProductVm
                {
                    ProductName = name,
                    Quantity = qty,
                });
            }

            return products;
        }
    }
}