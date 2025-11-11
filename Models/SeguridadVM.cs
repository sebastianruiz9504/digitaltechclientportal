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
}