using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

namespace DigitalTechClientPortal.Services
{
    public class DataverseSoporteService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        private string _accessToken = string.Empty;
        private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

        public DataverseSoporteService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        private async Task<string> GetAccessTokenAsync()
        {
            // Reutiliza el token mientras no esté cerca de expirar
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-2))
                return _accessToken;

            var url = _config["Dataverse:Url"];
            var clientId = _config["Dataverse:ClientId"];
            var clientSecret = _config["Dataverse:ClientSecret"];
            var tenantId = _config["Dataverse:TenantId"];

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Faltan valores de configuración para Dataverse.");
            }

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

        /// <summary>
        /// Ejecuta una consulta OData a Dataverse y devuelve el resultado como JsonDocument.
        /// Ejemplo: cr07a_tickets?$select=col1,col2&$filter=_cr07a_cliente_value eq {guid}&$orderby=colFecha desc
        /// </summary>
        public async Task<JsonDocument> GetAsync(string odataQuery)
        {
            var token = await GetAccessTokenAsync();
            var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/')
                          ?? throw new InvalidOperationException("Dataverse:Url no configurado.");

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

        /// <summary>
        /// Descarga un archivo almacenado en una columna de tipo file en Dataverse.
        /// GET {entitySet}({id})/{columnName}/$value
        /// </summary>
        public async Task<(Stream Stream, string ContentType, string FileName)?> GetFileAsync(string entitySet, Guid id, string columnName)
        {
            var token = await GetAccessTokenAsync();
            var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/')
                          ?? throw new InvalidOperationException("Dataverse:Url no configurado.");

            var url = $"{baseUrl}/api/data/v9.2/{entitySet}({id})/{columnName}/$value";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var mem = new MemoryStream();
            await resp.Content.CopyToAsync(mem);
            mem.Position = 0;

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            string fileName = $"archivo-{id}.bin";
            if (resp.Content.Headers.ContentDisposition != null)
            {
                fileName = resp.Content.Headers.ContentDisposition.FileNameStar
                           ?? resp.Content.Headers.ContentDisposition.FileName?.Trim('"')
                           ?? fileName;
            }
            else if (resp.Content.Headers.TryGetValues("Content-Disposition", out var vals))
            {
                var cd = string.Join(";", vals);
                const string tag = "filename=";
                var idx = cd.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var candidate = cd[(idx + tag.Length)..].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(candidate)) fileName = candidate;
                }
            }

            return (mem, contentType, fileName);
        }
         public async Task UploadFileAsync(
            string entitySet,
            Guid id,
            string columnName,
            Stream content,
            string fileName,
            string? contentType = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(entitySet)) throw new ArgumentException("Valor requerido", nameof(entitySet));
            if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentException("Valor requerido", nameof(columnName));
            if (content == null) throw new ArgumentNullException(nameof(content));

            var token = await GetAccessTokenAsync();
            var baseUrl = _config["Dataverse:Url"]?.TrimEnd('/')
                          ?? throw new InvalidOperationException("Dataverse:Url no configurado.");

            var url = $"{baseUrl}/api/data/v9.2/{entitySet}({id})/{columnName}/$value";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "*");
             client.DefaultRequestHeaders.TryAddWithoutValidation("Prefer", "return=minimal");
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-ms-file-name", fileName);
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            using var streamContent = new StreamContent(content);
            // Dataverse requiere application/octet-stream para las columnas de archivo.
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                streamContent.Headers.TryAddWithoutValidation("x-ms-file-content-type", contentType);
            }
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = streamContent
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Dataverse PATCH {response.StatusCode}. URL: {url}. Body: {body}");
            }
        }
    }
}