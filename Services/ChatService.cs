using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

using DigitalTechApp.Services; // para SearchService y SearchChunk

namespace DigitalTechApp.Services
{
    /// <summary>
    /// Orquestador RAG estricto:
    /// - Consulta SearchService para obtener chunks desde tus documentos.
    /// - Construye un contexto y llama a Azure OpenAI.
    /// - Si no hay evidencia suficiente, no responde con conocimiento externo.
    /// - Ofrece sugerencias (títulos del índice) al abrir el agente.
    /// </summary>
    public class ChatService
    {
        private readonly HttpClient _http;
        private readonly SearchService _search;

        // OpenAI
        private readonly string _oaEndpoint;    // https://<recurso>.openai.azure.com
        private readonly string _oaDeployment;  // o4-mini
        private readonly string _oaApiVersion;  // 2024-12-01-preview
        private readonly string? _oaApiKey;     // preferido (desde App Service)

        public ChatService(HttpClient httpClient, SearchService searchService)
        {
            _http = httpClient;
            _search = searchService;

            _oaEndpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT no configurado");
            _oaDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "o4-mini";
            _oaApiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-12-01-preview";
            _oaApiKey     = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"); // opcional
        }

        /// <summary>
        /// Responde únicamente con base en tus documentos (RAG estricto).
        /// </summary>
        public async Task<string> AnswerFromDocsAsync(string userQuestion, int top = 6, float minScore = 0.20f)
        {
            // 1) Buscar chunks relevantes
            var chunks = await _search.SearchChunksAsync(userQuestion, top);
            var filtered = FilterChunks(chunks, minScore);

            if (filtered.Count == 0)
                return "No encontré información en la base de conocimiento para esta consulta.";

            // 2) Construir contexto documental (limpio y limitado)
            var context = BuildContext(filtered, maxChars: 4500);

            // 3) Prompt que prohíbe conocimiento externo
            var systemPrompt =
                "Eres un asistente técnico que responde únicamente con base en el contexto proporcionado " +
                "desde documentos internos. Si no hay información suficiente, responde exactamente: " +
                "'No encontré información en la base de conocimiento'. No inventes ni uses conocimiento externo.";

            var userContent =
                $"Pregunta del usuario:\n{userQuestion}\n\n" +
                $"Contexto documental (extractos y títulos):\n{context}\n\n" +
                "Instrucciones: Responde paso a paso y claro, citando el título del documento relevante si aplica.";

            // 4) Llamada a OpenAI (o-series: chat/completions)
            var answer = await CallChatCompletionsAsync(systemPrompt, userContent, maxTokens: 800, temperature: 0.0);
            if (string.IsNullOrWhiteSpace(answer))
                return "No encontré información en la base de conocimiento.";

            return answer.Trim();
        }

        /// <summary>
        /// Sugerencias iniciales (títulos de documentos) para mostrar al abrir el agente.
        /// </summary>
        public async Task<List<string>> GetSuggestedTopicsAsync(int top = 8)
        {
            var titles = await _search.ListTitlesAsync(top * 3);
            var unique = new HashSet<string>(
                titles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase
            );

            return unique
                .Select(s => s.Length > 140 ? s[..140] + "…" : s)
                .Take(top)
                .ToList();
        }

        // ------------------------ Privados ------------------------

        private async Task<string> CallChatCompletionsAsync(string systemPrompt, string userContent, int maxTokens, double temperature)
        {
            var uri = $"{_oaEndpoint}/openai/deployments/{_oaDeployment}/chat/completions?api-version={_oaApiVersion}";

            var body = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userContent }
                },
                model = _oaDeployment,
                max_completion_tokens = maxTokens, // o-series
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            await AddOpenAIAuthAsync(req);

            using var res = await _http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException($"OpenAI chat/completions fallo ({(int)res.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return string.Empty;

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            return content ?? string.Empty;
        }

        private async Task AddOpenAIAuthAsync(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(_oaApiKey))
            {
                req.Headers.Add("api-key", _oaApiKey);
                return;
            }
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        private List<SearchChunk> FilterChunks(List<SearchChunk> input, float minScore)
        {
            return input
                .Where(c => c.Score >= minScore && !string.IsNullOrWhiteSpace(c.Chunk))
                .Select(c => new SearchChunk
                {
                    Title = SafeTitle(c.Title),
                    Chunk = NormalizeChunk(c.Chunk, maxPerChunkChars: 1300),
                    ParentId = c.ParentId,
                    Score = c.Score
                })
                .ToList();
        }

        private string BuildContext(List<SearchChunk> chunks, int maxChars)
        {
            var sb = new StringBuilder();
            foreach (var c in chunks)
            {
                sb.AppendLine($"[Documento] {c.Title}");
                sb.AppendLine(c.Chunk);
                sb.AppendLine();
            }
            var ctx = sb.ToString();
            return ctx.Length > maxChars ? ctx[..maxChars] + "…" : ctx;
        }

        private string NormalizeChunk(string text, int maxPerChunkChars)
        {
            var cleaned = (text ?? "").Replace("\r", " ").Replace("\n\n", "\n").Trim();
            if (cleaned.Length > maxPerChunkChars) cleaned = cleaned[..maxPerChunkChars] + "…";
            return cleaned;
        }

        private string SafeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "(sin título)";
            return title.Length > 200 ? title[..200] + "…" : title;
        }
    }
}