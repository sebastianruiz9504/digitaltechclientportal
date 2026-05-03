using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class SeguridadController : Controller
    {
        private readonly GraphClientFactory _graphDelegated;
        private readonly GraphPermissionService _graphPermissions;
        private readonly SecurityAiPlanService _securityAiPlan;
        private readonly ILogger<SeguridadController> _logger;

        public SeguridadController(
            GraphClientFactory graphDelegated,
            GraphPermissionService graphPermissions,
            SecurityAiPlanService securityAiPlan,
            ILogger<SeguridadController> logger)
        {
            _graphDelegated = graphDelegated;
            _graphPermissions = graphPermissions;
            _securityAiPlan = securityAiPlan;
            _logger = logger;
        }

        // ------------------ Vistas ------------------

        // Alertas: ahora devuelve SeguridadVM para que la vista Alertas.cshtml (tipada a SeguridadVM) funcione sin errores
        [HttpGet("Alertas")]
        public async Task<IActionResult> Alertas(
            [FromQuery] int top = 50,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (top <= 0) top = 50;
            if (top > 999) top = 999;

            var vm = new SeguridadVM
            {
                From = from?.Date,
                To = to?.Date,
            };

            vm.PermissionStatus = await _graphPermissions.GetSecurityPermissionStatusAsync();
            if (vm.PermissionStatus.HasMissingRequiredScopes)
            {
                if (!Request.Query.ContainsKey("consentChecked"))
                {
                    return RedirectToAction("Consent", "Login", new { returnUrl = BuildConsentReturnUrl() });
                }

                vm.GraphError = BuildMissingPermissionsMessage(vm.PermissionStatus);
                return View("Alertas", vm);
            }

            try
            {
                var client = await _graphDelegated.CreateClientAsync();
                vm.Alertas = await FetchAlerts(client, from, to, top, vm.DataSources);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible inicializar Microsoft Graph para Alertas.");
                vm.GraphError = "No fue posible conectar con Microsoft Graph. Inicia sesión nuevamente o valida el consentimiento de permisos.";
                vm.DataSources.Add(new SecurityDataSourceStatus
                {
                    Name = "Alertas",
                    IsAvailable = false,
                    Count = 0,
                    Message = vm.GraphError
                });
            }

            return View("Alertas", vm);
        }

        [HttpGet("ExportarAlertasCsv")]
        public async Task<IActionResult> ExportarAlertasCsv(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? search = null)
        {
            List<SecurityAlert> alertas;

            var permissionStatus = await _graphPermissions.GetSecurityPermissionStatusAsync();
            if (permissionStatus.HasMissingRequiredScopes)
            {
                return RedirectToAction("Consent", "Login", new { returnUrl = BuildConsentReturnUrl() });
            }

            try
            {
                var client = await _graphDelegated.CreateClientAsync();
                alertas = await FetchAlerts(client, from, to, 999);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible exportar alertas desde Microsoft Graph.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "No fue posible consultar Microsoft Graph para exportar alertas.");
            }

            alertas = FilterAlerts(alertas, from, to, severity, search)
                .OrderByDescending(a => a.CreatedDateTime ?? a.LastUpdatedDateTime ?? DateTimeOffset.MinValue)
                .ToList();

            var csv = new StringBuilder();
            csv.AppendLine("Titulo,Severidad,Categoria,Estado,Proveedor,Creada,Actualizada,Descripcion");

            foreach (var alerta in alertas)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    Csv(alerta.Title),
                    Csv(alerta.Severity),
                    Csv(alerta.Category),
                    Csv(alerta.Status),
                    Csv(alerta.Provider),
                    Csv(alerta.CreatedDateTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""),
                    Csv(alerta.LastUpdatedDateTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""),
                    Csv(alerta.Description)
                }));
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            var fileName = $"alertas-seguridad-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // Panel con todos los módulos
        [HttpGet("Panel")]
        public async Task<IActionResult> Panel(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int top = 50)
        {
            if (top <= 0) top = 50;
            if (top > 200) top = 200;

            var vm = new SeguridadVM
            {
                From = from?.Date,
                To = to?.Date
            };

            vm.PermissionStatus = await _graphPermissions.GetSecurityPermissionStatusAsync();
            if (vm.PermissionStatus.HasMissingRequiredScopes)
            {
                if (!Request.Query.ContainsKey("consentChecked"))
                {
                    return RedirectToAction("Consent", "Login", new { returnUrl = BuildConsentReturnUrl() });
                }

                vm.GraphError = BuildMissingPermissionsMessage(vm.PermissionStatus);
                return View("PanelSeguridad", vm);
            }

            try
            {
                var client = await _graphDelegated.CreateClientAsync();

                vm.Alertas = await FetchAlerts(client, from, to, top, vm.DataSources);
                vm.Incidentes = await FetchIncidents(client, from, to, top, vm.DataSources);
                vm.UsuariosRiesgo = await FetchRiskyUsers(client, top, vm.DataSources);
                vm.DispositivosRiesgo = await FetchDeviceSecurityStates(client, top, vm.DataSources);
                vm.SecureScores = await FetchSecureScores(client, top, vm.DataSources);
                vm.SecureScoreControles = await FetchSecureScoreControls(client, top, vm.DataSources);
                vm.SimulacionesAtaque = await FetchAttackSimulations(client, top, vm.DataSources);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible inicializar Microsoft Graph para el panel de seguridad.");
                vm.GraphError = "No fue posible conectar con Microsoft Graph. Inicia sesión nuevamente o valida el consentimiento de permisos.";
                vm.DataSources.Add(new SecurityDataSourceStatus
                {
                    Name = "Microsoft Graph",
                    IsAvailable = false,
                    Count = 0,
                    Message = vm.GraphError
                });
            }

            // Agregación mensual de Secure Score: último registro por mes
            vm.SecureScoreMensual = BuildSecureScoreMonthly(vm.SecureScores);

            return View("PanelSeguridad", vm);
        }

        [HttpGet("PlanTrabajo")]
        public async Task<IActionResult> PlanTrabajo(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int top = 80)
        {
            if (top <= 0) top = 80;
            if (top > 200) top = 200;

            var vm = new SeguridadVM
            {
                From = from?.Date,
                To = to?.Date
            };

            vm.PermissionStatus = await _graphPermissions.GetSecurityPermissionStatusAsync();
            if (vm.PermissionStatus.HasMissingRequiredScopes)
            {
                if (!Request.Query.ContainsKey("consentChecked"))
                {
                    return RedirectToAction("Consent", "Login", new { returnUrl = BuildConsentReturnUrl() });
                }

                vm.GraphError = BuildMissingPermissionsMessage(vm.PermissionStatus);
                return View("PlanTrabajo", vm);
            }

            try
            {
                var client = await _graphDelegated.CreateClientAsync();

                vm.Alertas = await FetchAlerts(client, from, to, top, vm.DataSources);
                vm.Incidentes = await FetchIncidents(client, from, to, top, vm.DataSources);
                vm.UsuariosRiesgo = await FetchRiskyUsers(client, top, vm.DataSources);
                vm.DispositivosRiesgo = await FetchDeviceSecurityStates(client, top, vm.DataSources);
                vm.SecureScores = await FetchSecureScores(client, top, vm.DataSources);
                vm.SecureScoreControles = await FetchSecureScoreControls(client, top, vm.DataSources);
                vm.SimulacionesAtaque = await FetchAttackSimulations(client, top, vm.DataSources);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible inicializar Microsoft Graph para el plan de trabajo de seguridad.");
                vm.GraphError = "No fue posible conectar con Microsoft Graph. Inicia sesión nuevamente o valida el consentimiento de permisos.";
                vm.DataSources.Add(new SecurityDataSourceStatus
                {
                    Name = "Microsoft Graph",
                    IsAvailable = false,
                    Count = 0,
                    Message = vm.GraphError
                });
            }

            vm.SecureScoreMensual = BuildSecureScoreMonthly(vm.SecureScores);

            if (string.IsNullOrWhiteSpace(vm.GraphError) && vm.DataSources.Any(s => s.IsAvailable))
            {
                try
                {
                    vm.PlanTrabajoAi = await _securityAiPlan.GenerateAsync(vm, HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo generar el plan de trabajo de seguridad con Azure OpenAI.");
                    vm.AiPlanError = "No se pudo generar el plan de trabajo con AI. Valida la configuración de Azure OpenAI y vuelve a intentarlo.";
                }
            }

            return View("PlanTrabajo", vm);
        }

        private static List<SecureScoreMonthly> BuildSecureScoreMonthly(IEnumerable<SecureScore> secureScores)
        {
            return secureScores
                .Where(s => s.CreatedDateTime.HasValue)
                .GroupBy(s => new { Year = s.CreatedDateTime!.Value.Year, Month = s.CreatedDateTime!.Value.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g =>
                {
                    var last = g.OrderBy(s => s.CreatedDateTime).Last();
                    return new SecureScoreMonthly
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        CurrentScore = last.CurrentScore,
                        MaxScore = last.MaxScore,
                        ActiveUserCount = last.ActiveUserCount
                    };
                })
                .ToList();
        }

        private static IEnumerable<SecurityAlert> FilterAlerts(
            IEnumerable<SecurityAlert> alertas,
            DateTime? from,
            DateTime? to,
            string? severity,
            string? search)
        {
            var query = alertas;

            if (from.HasValue)
            {
                var fromDate = from.Value.Date;
                query = query.Where(a => (a.CreatedDateTime ?? a.LastUpdatedDateTime)?.LocalDateTime >= fromDate);
            }

            if (to.HasValue)
            {
                var toDate = to.Value.Date.AddDays(1).AddSeconds(-1);
                query = query.Where(a => (a.CreatedDateTime ?? a.LastUpdatedDateTime)?.LocalDateTime <= toDate);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                var expected = NormalizeComparable(severity);
                query = query.Where(a => NormalizeComparable(a.Severity) == expected);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var text = search.Trim();
                query = query.Where(a =>
                    (a.Title ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (a.Category ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (a.Provider ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    (a.Status ?? "").Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            return query;
        }

        private static string NormalizeComparable(string? value)
        {
            return (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("í", "i")
                .Replace("á", "a")
                .Replace("é", "e")
                .Replace("ó", "o")
                .Replace("ú", "u");
        }

        private static string Csv(string? value)
        {
            var text = value ?? "";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private string BuildConsentReturnUrl()
        {
            var path = $"{Request.PathBase}{Request.Path}";
            var query = Request.QueryString.HasValue ? Request.QueryString.Value! : "";

            if (Request.Query.ContainsKey("consentChecked"))
                return path + query;

            var separator = string.IsNullOrWhiteSpace(query) ? "?" : "&";
            return $"{path}{query}{separator}consentChecked=1";
        }

        private static string BuildMissingPermissionsMessage(SecurityPermissionStatus permissionStatus)
        {
            return $"El token actual solo tiene {permissionStatus.GrantedRequiredCount} de {permissionStatus.RequiredCount} permisos requeridos para leer todo el módulo de seguridad. Se necesita consentimiento del tenant para continuar.";
        }

        // ------------------ Fetch helpers ------------------

        private static async Task<List<SecurityAlert>> FetchAlerts(
            HttpClient client,
            DateTime? from,
            DateTime? to,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Alertas";
            var list = new List<SecurityAlert>();
            var available = true;
            string? message = null;
            var url = $"https://graph.microsoft.com/v1.0/security/alerts_v2?$top={top}";
            var filters = new List<string>();

            if (from.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
                var fromStr = fromUtc.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'");
                filters.Add($"createdDateTime ge {fromStr}");
            }

            if (to.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
                var toStr = toUtc.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'");
                filters.Add($"createdDateTime le {toStr}");
            }

            if (filters.Count > 0) url += $"&$filter={string.Join(" and ", filters)}";

            string? nextLink = url;
            while (!string.IsNullOrEmpty(nextLink) && list.Count < top)
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    available = false;
                    message = BuildGraphWarning(resp.StatusCode, raw);
                    break;
                }

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in arr.EnumerateArray())
                    {
                        if (list.Count >= top) break;

                        list.Add(new SecurityAlert
                        {
                            Id = a.GetPropertyOrDefault("id"),
                            Title = TranslateTitle(a.GetPropertyOrDefault("title")),
                            Severity = TranslateSeverity(a.GetPropertyOrDefault("severity")),
                            Category = TranslateCategory(a.GetPropertyOrDefault("category")),
                            Status = TranslateStatus(a.GetPropertyOrDefault("status")),
                            CreatedDateTime = a.GetDateTimeOrNull("createdDateTime"),
                            LastUpdatedDateTime = a.GetDateTimeOrNull("lastUpdateDateTime") ?? a.GetDateTimeOrNull("lastUpdatedDateTime"),
                            FirstActivityDateTime = a.GetDateTimeOrNull("firstActivityDateTime"),
                            LastActivityDateTime = a.GetDateTimeOrNull("lastActivityDateTime"),
                            Provider = FirstNonEmpty(
                                a.GetPropertyOrDefault("productName"),
                                a.GetPropertyOrDefault("serviceSource"),
                                a.GetPropertyOrDefault("vendorInformation", "provider")),
                            ProductName = a.GetPropertyOrDefault("productName"),
                            ServiceSource = a.GetPropertyOrDefault("serviceSource"),
                            DetectionSource = a.GetPropertyOrDefault("detectionSource"),
                            IncidentId = a.GetPropertyOrDefault("incidentId"),
                            AlertWebUrl = a.GetPropertyOrDefault("alertWebUrl"),
                            IncidentWebUrl = a.GetPropertyOrDefault("incidentWebUrl"),
                            RecommendedActions = a.GetPropertyOrDefault("recommendedActions"),
                            MitreTechniques = a.GetArrayAsString("mitreTechniques"),
                            Description = a.GetPropertyOrDefault("description"),
                            Evidence = ExtractAlertEvidence(a)
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            RegisterDataSource(dataSources, sourceName, available, list.Count, message);
            return list;
        }

        private static async Task<List<SecurityIncident>> FetchIncidents(
            HttpClient client,
            DateTime? from,
            DateTime? to,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Incidentes";
            var list = new List<SecurityIncident>();
            var available = true;
            string? message = null;
            var url = $"https://graph.microsoft.com/v1.0/security/incidents?$top={top}&$expand=alerts";
            var filters = new List<string>();

            if (from.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
                var fromStr = fromUtc.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'");
                filters.Add($"createdDateTime ge {fromStr}");
            }

            if (to.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
                var toStr = toUtc.ToString("yyyy-MM-dd'T'HH':'mm':'ss'Z'");
                filters.Add($"lastUpdateDateTime le {toStr}");
            }

            if (filters.Count > 0) url += $"&$filter={string.Join(" and ", filters)}";

            string? nextLink = url;
            while (!string.IsNullOrEmpty(nextLink) && list.Count < top)
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    available = false;
                    message = BuildGraphWarning(resp.StatusCode, raw);
                    break;
                }

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in arr.EnumerateArray())
                    {
                        if (list.Count >= top) break;

                        list.Add(new SecurityIncident
                        {
                            Id = i.GetPropertyOrDefault("id"),
                            Title = TranslateTitle(FirstNonEmpty(i.GetPropertyOrDefault("displayName"), i.GetPropertyOrDefault("name"))),
                            Severity = TranslateSeverity(i.GetPropertyOrDefault("severity")),
                            Status = TranslateStatus(i.GetPropertyOrDefault("status")),
                            CreatedDateTime = i.GetDateTimeOrNull("createdDateTime"),
                            LastUpdatedDateTime = i.GetDateTimeOrNull("lastUpdateDateTime") ?? i.GetDateTimeOrNull("lastUpdatedDateTime"),
                            AlertCount = i.TryGetArrayCount("alerts", out var cnt) ? cnt : (int?)null,
                            AssignedTo = i.GetPropertyOrDefault("assignedTo"),
                            Classification = TranslateClassification(i.GetPropertyOrDefault("classification")),
                            Determination = TranslateDetermination(i.GetPropertyOrDefault("determination")),
                            Description = i.GetPropertyOrDefault("description"),
                            Summary = i.GetPropertyOrDefault("summary"),
                            IncidentWebUrl = i.GetPropertyOrDefault("incidentWebUrl")
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            RegisterDataSource(dataSources, sourceName, available, list.Count, message);
            return list;
        }

        private static async Task<List<RiskyUser>> FetchRiskyUsers(
            HttpClient client,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Usuarios en riesgo";
            var list = new List<RiskyUser>();
            var available = true;
            string? message = null;
            var url = $"https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink) && list.Count < top)
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    available = false;
                    message = BuildGraphWarning(resp.StatusCode, raw);
                    break;
                }

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in arr.EnumerateArray())
                    {
                        if (list.Count >= top) break;

                        list.Add(new RiskyUser
                        {
                            UserId = u.GetPropertyOrDefault("id"),
                            UserDisplayName = u.GetPropertyOrDefault("displayName"),
                            UserPrincipalName = u.GetPropertyOrDefault("userPrincipalName"),
                            RiskLevel = TranslateRiskLevel(u.GetPropertyOrDefault("riskLevel")),
                            RiskState = TranslateRiskState(u.GetPropertyOrDefault("riskState")),
                            RiskDetail = TranslateRiskDetail(u.GetPropertyOrDefault("riskDetail")),
                            LastUpdatedDateTime = u.GetDateTimeOrNull("lastUpdatedDateTime")
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            RegisterDataSource(dataSources, sourceName, available, list.Count, message);
            return list;
        }

        private static async Task<List<DeviceSecurityState>> FetchDeviceSecurityStates(
            HttpClient client,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Dispositivos";
            var list = new List<DeviceSecurityState>();
            var available = true;
            string? message = null;
            var url = $"https://graph.microsoft.com/v1.0/security/deviceSecurityStates?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink) && list.Count < top)
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    available = false;
                    message = BuildGraphWarning(resp.StatusCode, raw);
                    break;
                }

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in arr.EnumerateArray())
                    {
                        if (list.Count >= top) break;

                        list.Add(new DeviceSecurityState
                        {
                            DeviceId = d.GetPropertyOrDefault("deviceId"),
                            DeviceName = d.GetPropertyOrDefault("deviceName"),
                            LoggedOnUsers = d.GetPropertyOrDefault("loggedOnUsers"),
                            RiskScore = d.GetPropertyOrDefault("riskScore"),
                            MalwareState = TranslateMalwareState(d.GetPropertyOrDefault("malwareState")),
                            OS = d.GetPropertyOrDefault("os"),
                            LastSeenDateTime = d.GetDateTimeOrNull("lastSeenDateTime")
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            RegisterDataSource(dataSources, sourceName, available, list.Count, message);
            return list;
        }

        private static async Task<List<SecureScore>> FetchSecureScores(
            HttpClient client,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Secure Score";
            var list = new List<SecureScore>();
            var url = $"https://graph.microsoft.com/v1.0/security/secureScores?$top={top}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                RegisterDataSource(dataSources, sourceName, false, list.Count, BuildGraphWarning(resp.StatusCode, raw));
                return list;
            }

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in arr.EnumerateArray())
                {
                    if (list.Count >= top) break;

                    list.Add(new SecureScore
                    {
                        Id = s.GetPropertyOrDefault("id"),
                        ActiveUserCount = s.TryGetDouble("activeUserCount", out var auc) ? auc : (double?)null,
                        CurrentScore = s.TryGetDouble("currentScore", out var cs) ? cs : (double?)null,
                        MaxScore = s.TryGetDouble("maxScore", out var ms) ? ms : (double?)null,
                        EnabledServices = s.GetArrayAsString("enabledServices"),
                        CreatedDateTime = s.GetDateTimeOrNull("createdDateTime")
                    });
                }
            }

            RegisterDataSource(dataSources, sourceName, true, list.Count, null);
            return list;
        }

        private static async Task<List<SecureScoreControl>> FetchSecureScoreControls(
            HttpClient client,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Controles recomendados";
            var list = new List<SecureScoreControl>();
            var available = true;
            string? message = null;
            var url = $"https://graph.microsoft.com/v1.0/security/secureScoreControlProfiles?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink) && list.Count < top)
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    available = false;
                    message = BuildGraphWarning(resp.StatusCode, raw);
                    break;
                }

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in arr.EnumerateArray())
                    {
                        if (list.Count >= top) break;

                        list.Add(new SecureScoreControl
                        {
                            Id = c.GetPropertyOrDefault("id"),
                            Title = TranslateTitle(c.GetPropertyOrDefault("title")),
                            ControlCategory = TranslateCategory(c.GetPropertyOrDefault("controlCategory")),
                            ActionType = c.GetPropertyOrDefault("actionType"),
                            ImplementationCost = c.GetPropertyOrDefault("implementationCost"),
                            ControlStateUpdates = c.GetPropertyOrDefault("controlStateUpdates"),
                            MaxScore = c.TryGetDouble("maxScore", out var ms) ? ms : (double?)null
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            RegisterDataSource(dataSources, sourceName, available, list.Count, message);
            return list;
        }

        private static async Task<List<AttackSimulation>> FetchAttackSimulations(
            HttpClient client,
            int top,
            List<SecurityDataSourceStatus>? dataSources = null)
        {
            const string sourceName = "Simulaciones";
            var list = new List<AttackSimulation>();
            var url = $"https://graph.microsoft.com/v1.0/security/attackSimulation?$top={top}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                RegisterDataSource(dataSources, sourceName, false, list.Count, BuildGraphWarning(resp.StatusCode, raw));
                return list;
            }

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (list.Count >= top) break;

                    list.Add(new AttackSimulation
                    {
                        Id = a.GetPropertyOrDefault("id"),
                        DisplayName = TranslateTitle(a.GetPropertyOrDefault("displayName")),
                        Status = TranslateStatus(a.GetPropertyOrDefault("status")),
                        SimulationType = TranslateCategory(a.GetPropertyOrDefault("attackTechnique")),
                        LaunchDateTime = a.GetDateTimeOrNull("launchDateTime"),
                        CompletionDateTime = a.GetDateTimeOrNull("completionDateTime"),
                        Payload = a.GetPropertyOrDefault("payloadSource")
                    });
                }
            }

            RegisterDataSource(dataSources, sourceName, true, list.Count, null);
            return list;
        }

        private static void RegisterDataSource(
            List<SecurityDataSourceStatus>? dataSources,
            string name,
            bool isAvailable,
            int count,
            string? message)
        {
            if (dataSources == null) return;

            dataSources.Add(new SecurityDataSourceStatus
            {
                Name = name,
                IsAvailable = isAvailable,
                Count = count,
                Message = message ?? (count > 0 ? "Datos actualizados" : "Sin hallazgos recientes")
            });
        }

        private static string BuildGraphWarning(HttpStatusCode statusCode, string raw)
        {
            var detail = TryExtractGraphErrorMessage(raw);
            var baseMessage = statusCode switch
            {
                HttpStatusCode.Unauthorized => "Microsoft Graph devolvió 401. Inicia sesión nuevamente para renovar el token.",
                HttpStatusCode.Forbidden => "Microsoft Graph devolvió 403. Falta consentimiento o permisos para leer esta fuente.",
                HttpStatusCode.TooManyRequests => "Microsoft Graph limitó temporalmente la consulta. Intenta actualizar en unos minutos.",
                _ => $"Microsoft Graph devolvió {(int)statusCode}."
            };

            return string.IsNullOrWhiteSpace(detail)
                ? baseMessage
                : $"{baseMessage} {detail}";
        }

        private static string? TryExtractGraphErrorMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    {
                        var text = message.GetString();
                        return string.IsNullOrWhiteSpace(text)
                            ? null
                            : text.Length > 180 ? text.Substring(0, 180) + "..." : text;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static List<SecurityAlertEvidence> ExtractAlertEvidence(JsonElement alert)
        {
            var evidence = new List<SecurityAlertEvidence>();
            if (!alert.TryGetProperty("evidence", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return evidence;
            }

            foreach (var item in arr.EnumerateArray().Take(12))
            {
                var type = item.GetPropertyOrDefault("@odata.type")
                    .Replace("#microsoft.graph.security.", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Evidence", "", StringComparison.OrdinalIgnoreCase);

                evidence.Add(new SecurityAlertEvidence
                {
                    Type = Capitalize(type),
                    UserPrincipalName = FirstNonEmpty(
                        item.GetPropertyOrDefault("userPrincipalName"),
                        item.GetPropertyOrDefault("userAccount", "userPrincipalName"),
                        item.GetPropertyOrDefault("accountName"),
                        item.GetPropertyOrDefault("mailboxPrimaryAddress")),
                    UserDisplayName = FirstNonEmpty(
                        item.GetPropertyOrDefault("userDisplayName"),
                        item.GetPropertyOrDefault("displayName"),
                        item.GetPropertyOrDefault("accountName")),
                    DeviceName = FirstNonEmpty(
                        item.GetPropertyOrDefault("deviceDnsName"),
                        item.GetPropertyOrDefault("hostName"),
                        item.GetPropertyOrDefault("deviceName")),
                    IpAddress = FirstNonEmpty(
                        item.GetPropertyOrDefault("ipAddress"),
                        item.GetArrayAsString("ipInterfaces")),
                    Url = FirstNonEmpty(
                        item.GetPropertyOrDefault("url"),
                        item.GetPropertyOrDefault("domainName")),
                    FileName = item.GetPropertyOrDefault("fileDetails", "fileName"),
                    FilePath = item.GetPropertyOrDefault("fileDetails", "filePath"),
                    ProcessCommandLine = item.GetPropertyOrDefault("processCommandLine"),
                    Mailbox = FirstNonEmpty(
                        item.GetPropertyOrDefault("recipientEmailAddress"),
                        item.GetPropertyOrDefault("senderEmailAddress"),
                        item.GetPropertyOrDefault("mailboxPrimaryAddress")),
                    Roles = item.GetArrayAsString("roles"),
                    Verdict = TranslateStatus(item.GetPropertyOrDefault("verdict"))
                });
            }

            return evidence;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
        }

        // ------------------ Traducciones ------------------

        private static string TranslateSeverity(string sev) => sev?.ToLowerInvariant() switch
        {
            "critical" => "Crítica",
            "high" => "Alta",
            "medium" => "Media",
            "low" => "Baja",
            "informational" => "Informativa",
            "unknown" => "Desconocida",
            _ => Capitalize(sev)
        };

        private static string TranslateStatus(string status)
        {
            var s = status?.ToLowerInvariant();
            return s switch
            {
                "active" => "Activo",
                "new" => "Nueva",
                "inprogress" => "En progreso",
                "resolved" => "Resuelta",
                "unknown" => "Desconocido",
                "dismissed" => "Descartada",
                "completed" => "Completada",
                "launched" => "Lanzada",
                _ => Capitalize(status)
            };
        }

        private static string TranslateClassification(string classification)
        {
            var c = classification?.ToLowerInvariant();
            return c switch
            {
                "unknown" => "Sin clasificar",
                "falsepositive" => "Falso positivo",
                "truepositive" => "Verdadero positivo",
                "informationalexpectedactivity" => "Actividad esperada",
                _ => Capitalize(classification)
            };
        }

        private static string TranslateDetermination(string determination)
        {
            var d = determination?.ToLowerInvariant();
            return d switch
            {
                "unknown" => "Sin determinación",
                "apt" => "Amenaza persistente avanzada",
                "malware" => "Malware",
                "unwantedsoftware" => "Software no deseado",
                "multistagedattack" => "Ataque de varias etapas",
                "compromisedaccount" => "Cuenta comprometida",
                "phishing" => "Phishing",
                "malicioususeractivity" => "Actividad maliciosa de usuario",
                "notmalicious" => "No malicioso",
                "notenoughdatatovalidate" => "Sin datos suficientes",
                "confirmedactivity" => "Actividad confirmada",
                "lineofbusinessapplication" => "Aplicación de negocio confirmada",
                _ => Capitalize(determination)
            };
        }

        private static string TranslateCategory(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return "";
            var c = cat.ToLowerInvariant();
            return c switch
            {
                "malware" => "Malware",
                "phishing" => "Phishing",
                "ransomware" => "Ransomware",
                "suspiciousactivity" => "Actividad sospechosa",
                "unauthorizedaccess" => "Acceso no autorizado",
                "dataexfiltration" => "Exfiltración de datos",
                "identitycompromise" => "Compromiso de identidad",
                "commandandcontrol" => "Comando y control",
                "privilegeescalation" => "Escalada de privilegios",
                "configuration" => "Configuración",
                "email" => "Correo",
                "cloud app" => "Aplicación en la nube",
                _ => Capitalize(cat)
            };
        }

        private static string TranslateTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var t = title.ToLowerInvariant();
            if (t.Contains("malware")) return "Malware detectado";
            if (t.Contains("phishing")) return "Intento de phishing";
            if (t.Contains("ransomware")) return "Ransomware detectado";
            if (t.Contains("suspicious login")) return "Inicio de sesión sospechoso";
            if (t.Contains("suspicious")) return "Actividad sospechosa";
            if (t.Contains("unauthorized access")) return "Acceso no autorizado";
            if (t.Contains("data exfiltration")) return "Exfiltración de datos";
            if (t.Contains("brute force")) return "Intento de fuerza bruta";
            if (t.Contains("command and control")) return "Comando y control";
            if (t.Contains("privilege escalation")) return "Escalada de privilegios";
            if (t.Contains("identity compromise")) return "Compromiso de identidad";
            if (t.Contains("malicious")) return "Actividad maliciosa detectada";
            if (t.Contains("email")) return "Correo potencialmente malicioso";
            if (t.Contains("endpoint")) return "Alerta de endpoint";
            if (t.Contains("cloud app")) return "Alerta de aplicación en la nube";
            if (t.Contains("oauth app")) return "Aplicación OAuth sospechosa";
            if (t.Contains("url click")) return "Clic en URL potencialmente dañina";
            return Capitalize(title);
        }

        private static string TranslateRiskLevel(string level) => level?.ToLowerInvariant() switch
        {
            "low" => "Bajo",
            "medium" => "Medio",
            "high" => "Alto",
            _ => Capitalize(level)
        };

        private static string TranslateRiskState(string state)
        {
            var s = state?.ToLowerInvariant();
            return s switch
            {
                "atrisk" => "En riesgo",
                "confirmedcompromised" => "Compromiso confirmado",
                "remediated" => "Remediado",
                "dismissed" => "Descartado",
                _ => Capitalize(state)
            };
        }

        private static string TranslateRiskDetail(string detail)
        {
            var d = detail?.ToLowerInvariant();
            return d switch
            {
                "none" => "Sin detalle",
                "hidden" => "Oculto",
                "unknownfuturevalue" => "Desconocido",
                "admindismissedallriskforuser" => "Administrador descartó el riesgo",
                "userconfirmedsafe" => "Usuario confirmó seguro",
                _ => Capitalize(detail)
            };
        }

        private static string TranslateMalwareState(string state)
        {
            var s = state?.ToLowerInvariant();
            return s switch
            {
                "active" => "Activo",
                "cleaned" => "Limpio",
                "quarantined" => "Cuarentenado",
                "removed" => "Eliminado",
                _ => Capitalize(state)
            };
        }

        private static string Capitalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }

    // ---------------- JsonExtensions helpers ----------------
    internal static class JsonExtensions
    {
        public static string GetPropertyOrDefault(this JsonElement el, string name, string? inner = null)
        {
            if (el.TryGetProperty(name, out var p))
            {
                if (inner != null && p.ValueKind == JsonValueKind.Object && p.TryGetProperty(inner, out var inn))
                    return inn.GetString() ?? "";
                return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "") : p.ToString();
            }
            return "";
        }

        public static DateTimeOffset? GetDateTimeOrNull(this JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(p.GetString(), out var dto))
                    return dto;
            }
            return null;
        }

        public static bool TryGetInt(this JsonElement el, string name, out int value)
        {
            value = 0;
            if (el.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
                {
                    value = v;
                    return true;
                }
            }
            return false;
        }

        public static bool TryGetArrayCount(this JsonElement el, string name, out int value)
        {
            value = 0;
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array)
            {
                value = p.GetArrayLength();
                return true;
            }
            return false;
        }

        public static bool TryGetDouble(this JsonElement el, string name, out double value)
        {
            value = 0;
            if (el.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v))
                {
                    value = v;
                    return true;
                }
            }
            return false;
        }

        public static string GetArrayAsString(this JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var i in p.EnumerateArray())
                    items.Add(i.ValueKind == JsonValueKind.String ? (i.GetString() ?? "") : i.ToString());
                return string.Join(", ", items);
            }
            return "";
        }
    }
}
