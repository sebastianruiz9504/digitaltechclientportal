using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class SeguridadController : Controller
    {
        private readonly GraphClientFactory _graphDelegated;

        public SeguridadController(GraphClientFactory graphDelegated)
        {
            _graphDelegated = graphDelegated;
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

            var client = await _graphDelegated.CreateClientAsync();

            var vm = new SeguridadVM
            {
                From = from?.Date,
                To = to?.Date,
                Alertas = await FetchAlerts(client, from, to, top),

                // Inicializaciones para evitar nulls en la vista (coherencia con Panel)
                Incidentes = new List<SecurityIncident>(),
                UsuariosRiesgo = new List<RiskyUser>(),
                DispositivosRiesgo = new List<DeviceSecurityState>(),
                SecureScores = new List<SecureScore>(),
                SecureScoreControles = new List<SecureScoreControl>(),
                SimulacionesAtaque = new List<AttackSimulation>(),
                SecureScoreMensual = new List<SecureScoreMonthly>()
            };

            return View("Alertas", vm);
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

            var client = await _graphDelegated.CreateClientAsync();

            var vm = new SeguridadVM
            {
                From = from?.Date,
                To = to?.Date,
                Alertas = await FetchAlerts(client, from, to, top),
                Incidentes = await FetchIncidents(client, from, to, top),
                UsuariosRiesgo = await FetchRiskyUsers(client, top),
                DispositivosRiesgo = await FetchDeviceSecurityStates(client, top),
                SecureScores = await FetchSecureScores(client, top),
                SecureScoreControles = await FetchSecureScoreControls(client, top),
                SimulacionesAtaque = await FetchAttackSimulations(client, top)
            };

            // Agregación mensual de Secure Score: último registro por mes
            vm.SecureScoreMensual = vm.SecureScores
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

            return View("PanelSeguridad", vm);
        }

        // ------------------ Fetch helpers ------------------

        private static async Task<List<SecurityAlert>> FetchAlerts(HttpClient client, DateTime? from, DateTime? to, int top)
        {
            var list = new List<SecurityAlert>();
            var url = $"https://graph.microsoft.com/v1.0/security/alerts?$top={top}";
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
            while (!string.IsNullOrEmpty(nextLink))
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in arr.EnumerateArray())
                    {
                        list.Add(new SecurityAlert
                        {
                            Id = a.GetPropertyOrDefault("id"),
                            Title = TranslateTitle(a.GetPropertyOrDefault("title")),
                            Severity = TranslateSeverity(a.GetPropertyOrDefault("severity")),
                            Category = TranslateCategory(a.GetPropertyOrDefault("category")),
                            Status = TranslateStatus(a.GetPropertyOrDefault("status")),
                            CreatedDateTime = a.GetDateTimeOrNull("createdDateTime"),
                            LastUpdatedDateTime = a.GetDateTimeOrNull("lastUpdatedDateTime"),
                            Provider = a.GetPropertyOrDefault("vendorInformation", "provider"),
                            Description = a.GetPropertyOrDefault("description")
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            return list;
        }

        private static async Task<List<SecurityIncident>> FetchIncidents(HttpClient client, DateTime? from, DateTime? to, int top)
        {
            var list = new List<SecurityIncident>();
            var url = $"https://graph.microsoft.com/v1.0/security/incidents?$top={top}";
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
                filters.Add($"lastUpdatedDateTime le {toStr}");
            }

            if (filters.Count > 0) url += $"&$filter={string.Join(" and ", filters)}";

            string? nextLink = url;
            while (!string.IsNullOrEmpty(nextLink))
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in arr.EnumerateArray())
                    {
                        list.Add(new SecurityIncident
                        {
                            Id = i.GetPropertyOrDefault("id"),
                            Title = TranslateTitle(i.GetPropertyOrDefault("name")),
                            Severity = TranslateSeverity(i.GetPropertyOrDefault("severity")),
                            Status = TranslateStatus(i.GetPropertyOrDefault("status")),
                            CreatedDateTime = i.GetDateTimeOrNull("createdDateTime"),
                            LastUpdatedDateTime = i.GetDateTimeOrNull("lastUpdatedDateTime"),
                            AlertCount = i.TryGetInt("alerts", out var cnt) ? cnt : (int?)null
                        });
                    }
                }

                nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            }

            return list;
        }

        private static async Task<List<RiskyUser>> FetchRiskyUsers(HttpClient client, int top)
        {
            var list = new List<RiskyUser>();
            var url = $"https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink))
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in arr.EnumerateArray())
                    {
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

            return list;
        }

        private static async Task<List<DeviceSecurityState>> FetchDeviceSecurityStates(HttpClient client, int top)
        {
            var list = new List<DeviceSecurityState>();
            var url = $"https://graph.microsoft.com/v1.0/security/deviceSecurityStates?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink))
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in arr.EnumerateArray())
                    {
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

            return list;
        }

        private static async Task<List<SecureScore>> FetchSecureScores(HttpClient client, int top)
        {
            var list = new List<SecureScore>();
            var url = $"https://graph.microsoft.com/v1.0/security/secureScores?$top={top}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return list;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in arr.EnumerateArray())
                {
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

            return list;
        }

        private static async Task<List<SecureScoreControl>> FetchSecureScoreControls(HttpClient client, int top)
        {
            var list = new List<SecureScoreControl>();
            var url = $"https://graph.microsoft.com/v1.0/security/secureScoreControlProfiles?$top={top}";
            string? nextLink = url;

            while (!string.IsNullOrEmpty(nextLink))
            {
                var resp = await client.GetAsync(nextLink);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in arr.EnumerateArray())
                    {
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

            return list;
        }

        private static async Task<List<AttackSimulation>> FetchAttackSimulations(HttpClient client, int top)
        {
            var list = new List<AttackSimulation>();
            var url = $"https://graph.microsoft.com/v1.0/security/attackSimulation?$top={top}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return list;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
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

            return list;
        }

        // ------------------ Traducciones ------------------

        private static string TranslateSeverity(string sev) => sev?.ToLowerInvariant() switch
        {
            "high" => "Alta",
            "medium" => "Media",
            "low" => "Baja",
            _ => Capitalize(sev)
        };

        private static string TranslateStatus(string status)
        {
            var s = status?.ToLowerInvariant();
            return s switch
            {
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