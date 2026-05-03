using System.Globalization;
using System.Text.Json;
using DigitalTechApp.Services;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public sealed class M365OptimizationAiService
    {
        private const int PlanMaxTokens = 7500;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private readonly OpenAIClientAdapter _openAi;
        private readonly ILogger<M365OptimizationAiService> _logger;

        public M365OptimizationAiService(OpenAIClientAdapter openAi, ILogger<M365OptimizationAiService> logger)
        {
            _openAi = openAi;
            _logger = logger;
        }

        public async Task<M365OptimizationAiPlan> GenerateAsync(M365GovernanceVm vm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = BuildSnapshot(vm);
            var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);

            var systemPrompt = """
                Eres un consultor senior de adopción, gobierno y operación Microsoft 365.
                Tu tarea es convertir reportes técnicos de Microsoft Graph en un plan ejecutivo, claro y accionable en español.
                No inventes usuarios, fechas, cantidades ni fuentes. Si faltan datos o permisos, dilo en missingData.
                No estimes costos ni dinero. El foco es optimizar uso, adopción, gobierno, continuidad operativa y limpieza del tenant.
                Para pasos click a click usa portales conocidos: admin.microsoft.com, entra.microsoft.com, intune.microsoft.com, security.microsoft.com, compliance.microsoft.com.
                Devuelve solamente JSON válido con el contrato solicitado.
                """;

            var userPrompt = $"""
                Genera un plan de optimización Microsoft 365 basado exclusivamente en este JSON del tenant.

                Contrato de salida:
                - executiveSummary: resumen ejecutivo de 3 a 4 frases.
                - optimizationLevel: Bajo, Medio, Alto o Critico.
                - optimizationRationale: razonamiento corto.
                - generatedAtLocal: fecha/hora legible.
                - keyFindings: hallazgos principales por área, en lenguaje normal.
                - actions: acciones priorizadas con pasos click a click y validación.
                - missingData: datos o permisos que harían falta para mejorar el diagnóstico.
                - assumptions: supuestos hechos por falta de información.

                Reglas:
                - Máximo 5 hallazgos.
                - Máximo 6 acciones.
                - Cada acción debe tener entre 3 y 4 pasos.
                - Ningún campo de texto debe superar 350 caracteres.
                - Prioriza licencias sin uso, baja adopción, almacenamiento inactivo, políticas sin asignar, dispositivos no conformes, cambios de Microsoft y brechas de gobierno.
                - No incluyas ahorro en dinero ni precios.

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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo generar el plan de optimización Microsoft 365 con Azure OpenAI.");
                throw new InvalidOperationException($"Azure OpenAI no pudo generar el plan de optimización. {DescribeException(ex)}", ex);
            }
        }

        private static object BuildSnapshot(M365GovernanceVm vm)
        {
            var es = CultureInfo.GetCultureInfo("es-CO");
            return new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                generatedLocal = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm", es),
                period = vm.Period,
                periodDays = vm.PeriodDays,
                usageIdentityNote = vm.UsageIdentityNote,
                metrics = new
                {
                    purchasedLicenses = vm.TotalPurchasedLicenses,
                    assignedLicenses = vm.AssignedLicenses,
                    availableLicenses = vm.AvailableLicenses,
                    licensedEnabledUsers = vm.LicensedEnabledUsers,
                    guestUsers = vm.GuestUsers,
                    disabledUsers = vm.DisabledUsers,
                    usersWithoutCoreActivity = vm.UsersWithoutCoreActivity.Count,
                    largeInactiveOneDrive = vm.LargeInactiveOneDriveAccounts.Count,
                    largeInactiveSharePoint = vm.LargeInactiveSharePointSites.Count,
                    intunePolicies = vm.IntunePolicyCount,
                    unassignedPolicies = vm.UnassignedPolicies.Count,
                    managedDevices = vm.ManagedDevices.Count,
                    nonCompliantDevices = vm.NonCompliantDevices,
                    staleDevices = vm.StaleDevices,
                    activeServiceIssues = vm.ActiveServiceIssues,
                    majorUpcomingChanges = vm.MajorUpcomingChanges,
                    purviewCases = vm.Purview.EdiscoveryCases,
                    sensitivityLabels = vm.Purview.SensitivityLabels
                },
                dataSources = vm.DataSources.Select(s => new
                {
                    s.Name,
                    s.IsAvailable,
                    s.Count,
                    Message = Truncate(s.Message, 220)
                }),
                workloads = vm.Workloads.Select(w => new
                {
                    w.Name,
                    w.Area,
                    w.TotalItems,
                    w.ActiveItems,
                    w.InactiveItems,
                    w.ActivityPercent,
                    w.StorageUsedGb,
                    w.ActivityCount
                }),
                opportunities = vm.Opportunities
                    .OrderByDescending(o => SeverityRank(o.Severity))
                    .ThenByDescending(o => o.Count)
                    .Take(12)
                    .Select(o => new
                    {
                        o.Area,
                        o.Title,
                        o.Count,
                        o.Severity,
                        Detail = Truncate(o.Detail, 260),
                        RecommendedAction = Truncate(o.RecommendedAction, 260)
                    }),
                licenses = vm.Licenses
                    .OrderByDescending(l => l.AvailableUnits)
                    .Take(12)
                    .Select(l => new
                    {
                        l.DisplayName,
                        l.EnabledUnits,
                        l.ConsumedUnits,
                        l.AvailableUnits,
                        l.UtilizationPercent,
                        l.CapabilityStatus
                    }),
                usersWithoutCoreActivity = vm.UsersWithoutCoreActivity.Take(15).Select(u => new
                {
                    u.DisplayName,
                    u.UserPrincipalName,
                    u.LicenseCount,
                    Reason = Truncate(u.Reason, 180)
                }),
                largeInactiveOneDrive = vm.LargeInactiveOneDriveAccounts.Take(10).Select(o => new
                {
                    o.Owner,
                    o.OwnerPrincipalName,
                    o.StorageUsedGb,
                    LastActivity = o.LastActivityDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }),
                largeInactiveSharePoint = vm.LargeInactiveSharePointSites.Take(10).Select(s => new
                {
                    Name = Truncate(s.Name, 160),
                    s.Owner,
                    s.StorageUsedGb,
                    LastActivity = s.LastActivityDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                }),
                unassignedPolicies = vm.UnassignedPolicies.Take(10).Select(p => new
                {
                    p.Name,
                    p.PolicyType,
                    p.Platform,
                    p.Technology,
                    Modified = p.LastModifiedDateTime
                }),
                staleOrNonCompliantDevices = vm.StaleOrNonCompliantDevices.Take(10).Select(d => new
                {
                    d.DeviceName,
                    d.UserPrincipalName,
                    d.OperatingSystem,
                    d.ComplianceState,
                    d.LastSyncDateTime
                }),
                serviceHealth = vm.ServiceHealth.Where(h => !h.IsHealthy).Take(10).Select(h => new
                {
                    h.Service,
                    h.Status
                }),
                messageCenter = vm.MessageCenter.Take(10).Select(m => new
                {
                    Title = Truncate(m.Title, 180),
                    m.Category,
                    m.Severity,
                    m.IsMajorChange,
                    m.ActionRequiredByDateTime,
                    m.Services
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
                    name = "m365_optimization_plan",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "executiveSummary", "optimizationLevel", "optimizationRationale", "generatedAtLocal", "keyFindings", "actions", "missingData", "assumptions" },
                        properties = new Dictionary<string, object>
                        {
                            ["executiveSummary"] = new { type = "string" },
                            ["optimizationLevel"] = new { type = "string" },
                            ["optimizationRationale"] = new { type = "string" },
                            ["generatedAtLocal"] = new { type = "string" },
                            ["missingData"] = new { type = "array", items = new { type = "string" } },
                            ["assumptions"] = new { type = "array", items = new { type = "string" } },
                            ["keyFindings"] = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "area", "title", "plainLanguageSummary", "affectedPopulation", "whyItMatters" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["area"] = new { type = "string" },
                                        ["title"] = new { type = "string" },
                                        ["plainLanguageSummary"] = new { type = "string" },
                                        ["affectedPopulation"] = new { type = "string" },
                                        ["whyItMatters"] = new { type = "string" }
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
                                    required = new[] { "priority", "area", "title", "businessOutcome", "owner", "effort", "steps" },
                                    properties = new Dictionary<string, object>
                                    {
                                        ["priority"] = new { type = "string" },
                                        ["area"] = new { type = "string" },
                                        ["title"] = new { type = "string" },
                                        ["businessOutcome"] = new { type = "string" },
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

        private static M365OptimizationAiPlan ParsePlan(string raw)
        {
            var json = ExtractJson(raw);
            var plan = JsonSerializer.Deserialize<M365OptimizationAiPlan>(json, JsonOptions);
            return plan ?? throw new InvalidOperationException("Azure OpenAI devolvió una respuesta vacía para el plan de optimización.");
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
                throw new InvalidOperationException("Azure OpenAI no devolvió JSON válido para el plan de optimización.");
            }

            return text[start..(end + 1)];
        }

        private static string Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return cleaned.Length <= maxChars ? cleaned : cleaned[..maxChars] + "...";
        }

        private static int SeverityRank(string? value)
        {
            var v = (value ?? "").Trim().ToLowerInvariant();
            return v switch
            {
                "critico" or "crítico" => 4,
                "alta" or "alto" => 3,
                "media" or "medio" => 2,
                "baja" or "bajo" => 1,
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
    }
}
