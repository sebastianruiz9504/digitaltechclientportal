using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace DigitalTechApp.Services
{
    /// <summary>
    /// Adaptador pequeño para Azure OpenAI.
    /// Permite cambiar el contrato sin tocar ChatService.
    /// </summary>
    public class OpenAIClientAdapter
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;     // https://<resource>.openai.azure.com
        private readonly string _deployment;   // o4-mini
        private readonly string _apiVersion;   // 2024-12-01-preview
        private readonly string? _apiKey;      // preferido

        public OpenAIClientAdapter(HttpClient httpClient)
        {
            _http = httpClient;
            _endpoint   = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT no configurado");
            _deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "o4-mini";
            _apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-12-01-preview";
            _apiKey     = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"); // opcional
        }

        public async Task<string> ChatCompletionsAsync(string systemPrompt, string userContent, int maxTokens = 800, double temperature = 0.0)
        {
            var uri = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";

            var body = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userContent }
                },
                model = _deployment,
                max_completion_tokens = maxTokens, // o-series
                temperature = temperature
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            await AddAuthAsync(req);

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

        private async Task AddAuthAsync(HttpRequestMessage req)
        {
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                req.Headers.Add("api-key", _apiKey);
                return;
            }
            var credential = new DefaultAzureCredential();
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
    }
}