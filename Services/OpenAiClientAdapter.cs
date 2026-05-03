using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace DigitalTechApp.Services
{
    /// <summary>
    /// Adaptador pequeño para Azure OpenAI.
    /// Permite cambiar el contrato sin tocar ChatService.
    /// </summary>
    public class OpenAIClientAdapter
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly string _endpoint;     // https://<resource>.openai.azure.com
        private readonly string _deployment;   // o4-mini
        private readonly string _apiVersion;   // 2024-12-01-preview
        private readonly string? _apiKey;      // preferido
        private readonly bool _includeTemperature;
        private readonly string? _reasoningEffort;
        private readonly string? _verbosity;

        public OpenAIClientAdapter(HttpClient httpClient, IConfiguration configuration)
        {
            _http = httpClient;
            _configuration = configuration;
            _endpoint = (GetConfig("AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint")
                ?.TrimEnd('/'))
                ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT no configurado");
            _deployment = GetConfig("AZURE_OPENAI_DEPLOYMENT", "AZURE_OPENAI_CHAT_DEPLOYMENT", "AzureOpenAI:DeploymentName")
                ?? "o4-mini";
            _apiVersion = GetConfig("AZURE_OPENAI_API_VERSION", "AzureOpenAI:ApiVersion")
                ?? "2024-12-01-preview";
            _apiKey = GetConfig("AZURE_OPENAI_KEY", "AZURE_OPENAI_API_KEY", "AzureOpenAI:ApiKey"); // opcional

            _includeTemperature = bool.TryParse(GetConfig("AzureOpenAI:IncludeTemperature", "AZURE_OPENAI_INCLUDE_TEMPERATURE"), out var includeTemperature)
                ? includeTemperature
                : !IsReasoningDeployment(_deployment);

            _reasoningEffort = GetConfig("AzureOpenAI:ReasoningEffort", "AZURE_OPENAI_REASONING_EFFORT");
            _verbosity = GetConfig("AzureOpenAI:Verbosity", "AZURE_OPENAI_VERBOSITY");
        }

        public async Task<string> ChatCompletionsAsync(string systemPrompt, string userContent, int maxTokens = 800, double temperature = 0.0)
        {
            return await ChatCompletionsAsync(systemPrompt, userContent, maxTokens, temperature, responseFormat: null);
        }

        public async Task<string> ChatCompletionsJsonAsync(
            string systemPrompt,
            string userContent,
            object responseFormat,
            int maxTokens = 1800,
            double temperature = 0.0)
        {
            return await ChatCompletionsAsync(systemPrompt, userContent, maxTokens, temperature, responseFormat);
        }

        private async Task<string> ChatCompletionsAsync(
            string systemPrompt,
            string userContent,
            int maxTokens,
            double temperature,
            object? responseFormat)
        {
            var uri = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";

            var body = new Dictionary<string, object?>
            {
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userContent }
                },
                ["model"] = _deployment,
                ["max_completion_tokens"] = maxTokens // o-series y GPT-5
            };

            if (_includeTemperature)
            {
                body["temperature"] = temperature;
            }

            if (!string.IsNullOrWhiteSpace(_reasoningEffort))
            {
                body["reasoning_effort"] = _reasoningEffort;
            }

            if (!string.IsNullOrWhiteSpace(_verbosity))
            {
                body["verbosity"] = _verbosity;
            }

            if (responseFormat != null)
            {
                body["response_format"] = responseFormat;
            }

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

        private string? GetConfig(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = _configuration[key] ?? Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static bool IsReasoningDeployment(string deployment)
        {
            return deployment.StartsWith("o", StringComparison.OrdinalIgnoreCase)
                || deployment.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);
        }
    }
}
