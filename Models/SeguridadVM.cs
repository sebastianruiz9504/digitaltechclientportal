namespace DigitalTechClientPortal.Models
{
    // ViewModel principal del Panel de Seguridad
    public class SeguridadVM
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public List<SecurityAlert> Alertas { get; set; } = new();
        public List<SecurityIncident> Incidentes { get; set; } = new();
        public List<RiskyUser> UsuariosRiesgo { get; set; } = new();
        public List<DeviceSecurityState> DispositivosRiesgo { get; set; } = new();

        // Histórico crudo (si lo necesitas en otras vistas)
        public List<SecureScore> SecureScores { get; set; } = new();
        public List<SecureScoreControl> SecureScoreControles { get; set; } = new();
        public List<AttackSimulation> SimulacionesAtaque { get; set; } = new();

        // Agregación mensual de Secure Score para UI y gráficos
        public List<SecureScoreMonthly> SecureScoreMensual { get; set; } = new();

        // Estado de las consultas a Microsoft Graph. Permite diferenciar "sin hallazgos"
        // de "no se pudo leer esta fuente".
        public List<SecurityDataSourceStatus> DataSources { get; set; } = new();

        public string? GraphError { get; set; }
        public string? AiPlanError { get; set; }
        public SecurityAiPlan? PlanTrabajoAi { get; set; }
        public SecurityPermissionStatus PermissionStatus { get; set; } = new();

        // KPIs rápidos para tarjetas (nullable para poder usar ?? en Razor)
        public int? KpiAlertasTotal => Alertas?.Count;
        public int? KpiUsuariosRiesgoTotal => UsuariosRiesgo?.Count;
        public int? KpiDispositivosRiesgoTotal => DispositivosRiesgo?.Count;

        // Secure Score actual en porcentaje (nullable double)
        public double? KpiSecureScoreActualPct
        {
            get
            {
                var last = SecureScoreMensual.LastOrDefault();
                if (last == null || !last.CurrentScore.HasValue || !last.MaxScore.HasValue || last.MaxScore.Value == 0)
                    return null;

                return Math.Round(100.0 * last.CurrentScore.Value / last.MaxScore.Value, 2);
            }
        }

        // Si quieres seguir mostrando el valor formateado como antes
        public string? KpiSecureScoreActualFormatted => SecureScoreMensual.LastOrDefault()?.CurrentScoreFormatted;

        public DateTimeOffset? LastSecuritySignalUtc
        {
            get
            {
                var dates = new List<DateTimeOffset>();

                dates.AddRange(Alertas.Select(a => a.LastUpdatedDateTime ?? a.CreatedDateTime).Where(d => d.HasValue).Select(d => d!.Value));
                dates.AddRange(Incidentes.Select(i => i.LastUpdatedDateTime ?? i.CreatedDateTime).Where(d => d.HasValue).Select(d => d!.Value));
                dates.AddRange(UsuariosRiesgo.Select(u => u.LastUpdatedDateTime).Where(d => d.HasValue).Select(d => d!.Value));
                dates.AddRange(DispositivosRiesgo.Select(d => d.LastSeenDateTime).Where(d => d.HasValue).Select(d => d!.Value));
                dates.AddRange(SecureScores.Select(s => s.CreatedDateTime).Where(d => d.HasValue).Select(d => d!.Value));
                dates.AddRange(SimulacionesAtaque.Select(s => s.CompletionDateTime ?? s.LaunchDateTime).Where(d => d.HasValue).Select(d => d!.Value));

                return dates.Count == 0 ? null : dates.Max();
            }
        }
    }

    public class SecureScoreMonthly
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double? CurrentScore { get; set; }
        public double? MaxScore { get; set; }
        public double? ActiveUserCount { get; set; }

        public string Label => $"{Year}-{Month.ToString().PadLeft(2, '0')}";
        public string CurrentScoreFormatted => CurrentScore.HasValue ? CurrentScore.Value.ToString("0.##") : "-";
        public string MaxScoreFormatted => MaxScore.HasValue ? MaxScore.Value.ToString("0.##") : "-";
    }

    public class SecurityDataSourceStatus
    {
        public string Name { get; set; } = "";
        public bool IsAvailable { get; set; }
        public int Count { get; set; }
        public string Message { get; set; } = "";
    }

    public class SecurityPermissionStatus
    {
        public List<string> RequiredScopes { get; set; } = new();
        public List<string> GrantedScopes { get; set; } = new();
        public List<string> MissingScopes { get; set; } = new();
        public Dictionary<string, string> ScopeDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasMissingRequiredScopes => MissingScopes.Count > 0;
        public int GrantedRequiredCount => RequiredScopes.Count - MissingScopes.Count;
        public int RequiredCount => RequiredScopes.Count;
    }
}
