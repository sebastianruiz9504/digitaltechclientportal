using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace DigitalTechApp.Services
{
    /// <summary>
    /// Encapsula llamadas a Azure Cognitive Search mediante RBAC (AAD/Managed Identity).
    /// - Busca chunks relevantes.
    /// - Lista títulos para sugerencias.
    /// Corrige el 400 removiendo @search.score de $select.
    /// </summary>
    public class SearchService
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;   // https://<service>.search.windows.net
        private readonly string _index;      // rag-...
        private readonly string _apiVersion; // 2023-11-01

        public SearchService(HttpClient httpClient)
        {
            _http       = httpClient;
            _endpoint   = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT") ?? throw new InvalidOperationException("AZURE_SEARCH_ENDPOINT no configurado");
            _index      = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX") ?? throw new InvalidOperationException("AZURE_SEARCH_INDEX no configurado");
            _apiVersion = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_VERSION") ?? "2023-11-01";
        }

        public async Task<List<SearchChunk>> SearchChunksAsync(string query, int top = 6)
        {
            // Solo campos de esquema; NO incluir @search.score en $select
            var select = "title,chunk,chunk_id,parent_id";
            var uri = $"{_endpoint}/indexes/{_index}/docs" +
                      $"?api-version={_apiVersion}" +
                      $"&search={Uri.EscapeDataString(query)}" +
                      $"&$top={top}" +
                      $"&$select={Uri.EscapeDataString(select)}" +
                      $"&$orderby=search.score() desc"; // opcional, ayuda a relevancia

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            await AddAuthAsync(req);

            using var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Search fallo ({(int)res.StatusCode}): {json}");

            var list = new List<SearchChunk>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return list;

            foreach (var item in arr.EnumerateArray())
            {
                var chunk = new SearchChunk
                {
                    Title    = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Chunk    = item.TryGetProperty("chunk", out var c) ? c.GetString() ?? "" : "",
                    ParentId = item.TryGetProperty("parent_id", out var p) ? p.GetString() ?? "" : "",
                    // El score viene en la respuesta como anotación y se puede leer
                    Score    = item.TryGetProperty("@search.score", out var s) ? (float)s.GetDouble() : 0f
                };
                if (!string.IsNullOrWhiteSpace(chunk.Chunk))
                    list.Add(chunk);
            }

            return list.OrderByDescending(x => x.Score).ToList();
        }

        public async Task<List<string>> ListTitlesAsync(int top = 20)
        {
            var select = "title"; // sólo campos del esquema
            var uri = $"{_endpoint}/indexes/{_index}/docs" +
                      $"?api-version={_apiVersion}" +
                      $"&search={Uri.EscapeDataString("*")}" +
                      $"&$top={top}" +
                      $"&$select={Uri.EscapeDataString(select)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            await AddAuthAsync(req);

            using var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"Search fallo ({(int)res.StatusCode}): {json}");

            var titles = new List<string>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("value", out var arr)) return titles;

            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("title", out var t))
                {
                    var title = t.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                        titles.Add(title.Trim());
                }
            }
            return titles;
        }

        private async Task AddAuthAsync(HttpRequestMessage req)
        {
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://search.azure.com/.default" }));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
    }

    /// <summary>
    /// POCO para resultados de búsqueda.
    /// </summary>
    public sealed class SearchChunk
    {
        public string Title { get; set; } = "";
        public string Chunk { get; set; } = "";
        public string ParentId { get; set; } = "";
        public float Score { get; set; }
    }
}