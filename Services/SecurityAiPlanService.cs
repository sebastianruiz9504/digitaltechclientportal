using System.Globalization;
using System.Text.Json;
using DigitalTechApp.Services;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public sealed class SecurityAiPlanService
    {
        private const int PlanMaxTokens = 9000;
        private const int FallbackMaxTokens = 7000;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private readonly OpenAIClientAdapter _openAi;
        private readonly ILogger<SecurityAiPlanService> _logger;

        public SecurityAiPlanService(OpenAIClientAdapter openAi, ILogger<SecurityAiPlanService> logger)
        {
            _openAi = openAi;
            _logger = logger;
        }

        public async Task<SecurityAiPlan> GenerateAsync(SeguridadVM vm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = BuildSnapshot(vm);
            var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

            var systemPrompt = """
                Eres un consultor senior de ciberseguridad Microsoft 365 para clientes empresariales.
                Tu tarea es transformar datos técnicos de Microsoft Graph en un plan de trabajo claro, accionable y en español.
                No inventes hallazgos, usuarios, fechas ni evidencias. Si faltan datos, dilo en missingData.
                No digas que ejecutaste acciones. Entrega recomendaciones para que el administrador las ejecute.
                Prioriza acciones con impacto real en postura de seguridad: identidad, correo, endpoint, configuración y respuesta a incidentes.
                Explica cada amenaza en lenguaje normal y traduce los términos técnicos al impacto para negocio.
                Para pasos click a click usa portales Microsoft conocidos: security.microsoft.com, entra.microsoft.com, intune.microsoft.com, admin.microsoft.com.
                Devuelve solamente JSON válido con el contrato solicitado.
                """;

            var userPrompt = $"""
                Genera un plan de trabajo de seguridad basado exclusivamente en este JSON del tenant.

                Contrato de salida:
                - executiveSummary: resumen ejecutivo de 3 a 4 frases.
                - tenantRiskLevel: Bajo, Medio, Alto o Critico.
                - tenantRiskRationale: razonamiento corto.
                - generatedAtLocal: fecha/hora legible.
                - threats: amenazas principales con usuario, dispositivo, fecha, evidencia, impacto y acción inmediata.
                - actions: acciones priorizadas con pasos click a click y validación.
                - missingData: datos que harían falta para mejorar el diagnóstico.
                - assumptions: supuestos hechos por falta de información.

                Reglas:
                - Máximo 4 amenazas.
                - Máximo 5 acciones.
                - Cada acción debe tener entre 3 y 4 pasos.
                - Escribe frases cortas. Ningún campo de texto debe superar 350 caracteres.
                - En steps, clickPath, instruction y validation deben ser concretos y breves.
                - No uses lenguaje alarmista.
                - No incluyas recomendaciones genéricas si no se conectan con los datos.
                - Si no hay alertas/incidentes, enfócate en Secure Score, controles y revisión preventiva.

                JSON del tenant:
                {snapshotJson}
                """;

            try
            {
                var response = await _openAi.ChatCompletionsJsonAsync(
                    systemPrompt,
                    userPrompt,
                    BuildResponseFormat(),
                    maxTokens: PlanMaxTokens,
                    temperature: 0);

                return ParsePlan(response);
            }
            catch (Exception ex) when (IsTokenLimitError(ex))
            {
                _logger.LogWarning(ex, "Azure OpenAI cortó el plan de seguridad por límite de tokens.");
                throw new InvalidOperationException($"Structured outputs: {DescribeException(ex)}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo generar el plan de seguridad con structured outputs; se intentará respuesta JSON simple.");
                try
                {
                    var fallbackPrompt = userPrompt + "\n\nDevuelve solamente un objeto JSON. No uses Markdown ni texto antes o después del JSON.";
                    var fallback = await _openAi.ChatCompletionsAsync(systemPrompt, fallbackPrompt, maxTokens: FallbackMaxTokens, temperature: 0);
                    return ParsePlan(fallback);
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "No se pudo generar el plan de seguridad con respuesta JSON simple.");
                    throw new InvalidOperationException(
                        $"Structured outputs: {DescribeException(ex)}. Fallback JSON: {DescribeException(fallbackEx)}",
                        fallbackEx);
                }
            }
        }

        private static object BuildSnapshot(SeguridadVM vm)
        {
            var es = CultureInfo.GetCultureInfo("es-CO");
            var latestScore = vm.SecureScoreMensual.LastOrDefault();
            var scorePct = latestScore?.CurrentScore is double current && latestScore.MaxScore is double max && max > 0
                ? Math.Round(current * 100 / max, 2)
                : (double?)null;

            return new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                generatedLocal = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm", es),
                range = new
                {
                    from = vm.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    to = vm.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                },
                metrics = new
                {
                    alerts = vm.Alertas.Count,
                    highAlerts = vm.Alertas.Count(a => IsHigh(a.Severity)),
                    activeIncidents = vm.Incidentes.Count(i => !IsResolved(i.Status)),
                    riskyUsers = vm.UsuariosRiesgo.Count,
                    highRiskUsers = vm.UsuariosRiesgo.Count(u => IsHigh(u.RiskLevel)),
                    riskyDevices = vm.DispositivosRiesgo.Count,
                    secureScorePercent = scorePct,
                    lastSignalUtc = vm.LastSecuritySignalUtc
                },
                dataSources = vm.DataSources.Select(s => new
                {
                    s.Name,
                    s.IsAvailable,
                    s.Count,
                    Message = Truncate(s.Message, 260)
                }),
                alerts = vm.Alertas
                    .OrderByDescending(a => SeverityRank(a.Severity))
                    .ThenByDescending(a => a.CreatedDateTime ?? a.LastUpdatedDateTime ?? DateTimeOffset.MinValue)
                    .Take(8)
                    .Select(a => new
                    {
                        a.Id,
                        a.IncidentId,
                        Title = Truncate(a.Title, 140),
                        a.Severity,
                        a.Status,
                        a.Category,
                        Created = a.CreatedDateTime,
                        Updated = a.LastUpdatedDateTime,
                        FirstActivity = a.FirstActivityDateTime,
                        LastActivity = a.LastActivityDateTime,
                        Provider = FirstNonEmpty(a.ProductName, a.Provider, a.ServiceSource),
                        a.ServiceSource,
                        a.DetectionSource,
                        a.MitreTechniques,
                        Description = Truncate(a.Description, 260),
                        RecommendedActions = Truncate(a.RecommendedActions, 320),
                        Evidence = a.Evidence.Take(3).Select(e => new
                        {
                            e.Type,
                            e.UserPrincipalName,
                            e.UserDisplayName,
                            e.DeviceName,
                            e.IpAddress,
                            e.Url,
                            e.FileName,
                            e.FilePath,
                            e.ProcessCommandLine,
                            e.Mailbox,
                            e.Roles,
                            e.Verdict
                        })
                    }),
                incidents = vm.Incidentes
                    .OrderByDescending(i => SeverityRank(i.Severity))
                    .ThenByDescending(i => i.CreatedDateTime ?? i.LastUpdatedDateTime ?? DateTimeOffset.MinValue)
                    .Take(5)
                    .Select(i => new
                    {
                        i.Id,
                        Title = Truncate(i.Title, 140),
                        i.Severity,
                        i.Status,
                        Created = i.CreatedDateTime,
                        Updated = i.LastUpdatedDateTime,
                        i.AlertCount,
                        i.AssignedTo,
                        i.Classification,
                        i.Determination,
                        Description = Truncate(i.Description, 260),
                        Summary = Truncate(i.Summary, 260)
                    }),
                riskyUsers = vm.UsuariosRiesgo
                    .OrderByDescending(u => SeverityRank(u.RiskLevel))
                    .ThenByDescending(u => u.LastUpdatedDateTime ?? DateTimeOffset.MinValue)
                    .Take(8)
                    .Select(u => new
                    {
                        u.UserDisplayName,
                        u.UserPrincipalName,
                        u.RiskLevel,
                        u.RiskState,
                        u.RiskDetail,
                        Updated = u.LastUpdatedDateTime
                    }),
                riskyDevices = vm.DispositivosRiesgo
                    .OrderByDescending(d => d.LastSeenDateTime ?? DateTimeOffset.MinValue)
                    .Take(8)
                    .Select(d => new
                    {
                        d.DeviceName,
                        d.LoggedOnUsers,
                        d.RiskScore,
                        d.MalwareState,
                        d.OS,
                        LastSeen = d.LastSeenDateTime
                    }),
                secureScore = new
                {
                    Percent = scorePct,
                    Current = latestScore?.CurrentScore,
                    Max = latestScore?.MaxScore,
                    Trend = vm.SecureScoreMensual.TakeLast(6).Select(s => new
                    {
                        s.Label,
                        s.CurrentScore,
                        s.MaxScore
                    })
                },
                secureScoreControls = vm.SecureScoreControles
                    .OrderByDescending(c => c.MaxScore ?? 0)
                    .Take(6)
                    .Select(c => new
                    {
                        Title = Truncate(c.Title, 140),
                        c.ControlCategory,
                        c.ActionType,
                        c.ImplementationCost,
                        c.ControlStateUpdates,
                        c.MaxScore
                    }),
                attackSimulations = vm.SimulacionesAtaque
                    .OrderByDescending(s => s.LaunchDateTime ?? s.CompletionDateTime ?? DateTimeOffset.MinValue)
                    .Take(3)
                    .Select(s => new
                    {
                        s.DisplayName,
                        s.Status,
                        s.SimulationType,
                        s.LaunchDateTime,
                        s.CompletionDateTime,
                        s.Payload
                    })
            };
        }

        private static object BuildResponseFormat()
        {
            return new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "security_work_plan",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "executiveSummary", "tenantRiskLevel", "tenantRiskRationale", "generatedAtLocal", "threats", "actions", "missingData", "assumptions" },
                        properties = new Dictionary<string, object>
                        {
                            ["executiveSummary"] = new { type = "string" },
                            ["tenantRiskLevel"] = new { type = "string" },
                            ["tenantRiskRationale"] = new { type = "string" },
                            ["generatedAtLocal"] = new { type = "string" },
                            ["missingData"] = new { type = "array", items = new { type = "string" } },
                            ["assumptions"] = new { type = "array", items = new { type = "string" } },
                            ["threats"] = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "title", "plainLanguageSummary", "threatTranslation", "affectedUser", "affectedDevice", "date", "severity", "businessImpact", "evidence", "immediateAction" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["title"] = new { type = "string" },
                                        ["plainLanguageSummary"] = new { type = "string" },
                                        ["threatTranslation"] = new { type = "string" },
                                        ["affectedUser"] = new { type = "string" },
                                        ["affectedDevice"] = new { type = "string" },
                                        ["date"] = new { type = "string" },
                                        ["severity"] = new { type = "string" },
                                        ["businessImpact"] = new { type = "string" },
                                        ["evidence"] = new { type = "string" },
                                        ["immediateAction"] = new { type = "string" }
                                    }
                                }
                            },
                            ["actions"] = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "priority", "title", "whyItMatters", "expectedOutcome", "owner", "effort", "steps" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["priority"] = new { type = "string" },
                                        ["title"] = new { type = "string" },
                                        ["whyItMatters"] = new { type = "string" },
                                        ["expectedOutcome"] = new { type = "string" },
                                        ["owner"] = new { type = "string" },
                                        ["effort"] = new { type = "string" },
                                        ["steps"] = new
                                        {
                                            type = "array",
                                            items = new
                                            {
                                                type = "object",
                                                additionalProperties = false,
                                                required = new[] { "order", "portal", "clickPath", "instruction", "validation" },
                                                properties = new Dictionary<string, object>
                                                {
                                                    ["order"] = new { type = "integer" },
                                                    ["portal"] = new { type = "string" },
                                                    ["clickPath"] = new { type = "string" },
                                                    ["instruction"] = new { type = "string" },
                                                    ["validation"] = new { type = "string" }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static SecurityAiPlan ParsePlan(string raw)
        {
            var json = ExtractJson(raw);
            var plan = JsonSerializer.Deserialize<SecurityAiPlan>(json, JsonOptions);

            if (plan == null)
            {
                throw new InvalidOperationException("Azure OpenAI devolvió una respuesta vacía para el plan de seguridad.");
            }

            return plan;
        }

        private static string ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Azure OpenAI devolvió una respuesta vacía.");
            }

            var text = raw.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = text.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    text = text[(firstLineEnd + 1)..];
                }

                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                {
                    text = text[..fence];
                }
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');

            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("Azure OpenAI no devolvió JSON válido para el plan de seguridad.");
            }

            return text[start..(end + 1)];
        }

        private static bool IsHigh(string? value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            return v is "critical" or "crítico" or "critico" or "high" or "alta" or "alto";
        }

        private static bool IsResolved(string? value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            return v is "resolved" or "resuelta" or "dismissed" or "descartada" or "closed" or "cerrado";
        }

        private static string Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            var cleaned = value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            return cleaned.Length <= maxChars ? cleaned : cleaned[..maxChars] + "...";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        }

        private static int SeverityRank(string? value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            return v switch
            {
                "critical" or "crítica" or "critico" or "crítico" => 4,
                "high" or "alta" or "alto" => 3,
                "medium" or "media" or "medio" => 2,
                "low" or "baja" or "bajo" => 1,
                _ => 0
            };
        }

        private static string DescribeException(Exception ex)
        {
            return ex switch
            {
                OpenAiCallException openAiEx => openAiEx.ToUserMessage(),
                JsonException jsonEx => $"JSON inválido: {jsonEx.Message}",
                _ => ex.Message
            };
        }

        private static bool IsTokenLimitError(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is OpenAiCallException && current.Message.Contains("límite de tokens", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (current.Message.Contains("cortó la respuesta", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("limite de tokens", StringComparison.OrdinalIgnoreCase)
                    || current.Message.Contains("límite de tokens", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
