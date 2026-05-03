using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<OpenAIClientAdapter> _logger;
        private readonly string _endpoint;     // https://<resource>.openai.azure.com
        private readonly string _deployment;   // o4-mini
        private readonly string _apiVersion;   // 2024-12-01-preview
        private readonly string? _apiKey;      // preferido
        private readonly bool _includeTemperature;
        private readonly string? _reasoningEffort;
        private readonly string? _verbosity;
        private readonly TimeSpan _timeout;

        public OpenAIClientAdapter(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<OpenAIClientAdapter> logger)
        {
            _http = httpClient;
            _configuration = configuration;
            _logger = logger;
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
            _timeout = int.TryParse(GetConfig("AzureOpenAI:TimeoutSeconds", "AZURE_OPENAI_TIMEOUT_SECONDS"), out var timeoutSeconds) && timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeSpan.FromSeconds(220);

            _http.Timeout = _timeout;
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

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req);
            }
            catch (TaskCanceledException ex)
            {
                throw new OpenAiCallException(
                    $"La llamada a Azure OpenAI superó el timeout de {_timeout.TotalSeconds:0} segundos.",
                    innerException: ex);
            }

            using (res)
            {
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                var (errorCode, message) = TryExtractError(json);
                throw new OpenAiCallException(
                    message ?? $"Azure OpenAI devolvió una respuesta no exitosa. Detalle: {Truncate(json, 700)}",
                    res.StatusCode,
                    errorCode,
                    Truncate(json, 1200));
            }

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return string.Empty;

            var choice = choices[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
            LogUsage(doc.RootElement, finishReason);
            if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenAiCallException("Azure OpenAI bloqueó la respuesta por filtros de contenido.");
            }

            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                throw new OpenAiCallException("Azure OpenAI cortó la respuesta por límite de tokens. Reduce el rango o vuelve a generar el plan.");
            }

            return content ?? string.Empty;
            }
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

        private static (string? Code, string? Message) TryExtractError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return (null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("error", out var error))
                {
                    return (null, null);
                }

                var code = error.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString()
                    : null;
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;

                return (code, message);
            }
            catch
            {
                return (null, null);
            }
        }

        private void LogUsage(JsonElement root, string? finishReason)
        {
            if (!root.TryGetProperty("usage", out var usage))
            {
                return;
            }

            var promptTokens = TryGetInt(usage, "prompt_tokens");
            var completionTokens = TryGetInt(usage, "completion_tokens");
            var totalTokens = TryGetInt(usage, "total_tokens");
            int? reasoningTokens = null;

            if (usage.TryGetProperty("completion_tokens_details", out var completionDetails))
            {
                reasoningTokens = TryGetInt(completionDetails, "reasoning_tokens");
            }

            _logger.LogInformation(
                "Azure OpenAI usage. Deployment={Deployment}, FinishReason={FinishReason}, PromptTokens={PromptTokens}, CompletionTokens={CompletionTokens}, ReasoningTokens={ReasoningTokens}, TotalTokens={TotalTokens}",
                _deployment,
                finishReason ?? "",
                promptTokens,
                completionTokens,
                reasoningTokens,
                totalTokens);
        }

        private static int? TryGetInt(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            return value.TryGetInt32(out var parsed) ? parsed : null;
        }

        private static string Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return cleaned.Length <= maxChars ? cleaned : cleaned[..maxChars] + "...";
        }
    }
}
