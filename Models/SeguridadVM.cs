using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public sealed class SeguridadVm
    {
        // Métricas principales
        public int SecureScoreCurrent { get; set; }
        public int AdoptionScoreCurrent { get; set; }
        public int ThreatsLast30Days { get; set; }
        public int SecureScoreDelta { get; set; }
        public int AdoptionScoreDelta { get; set; }

        // Series para gráficos
        public List<string> TimelineLabels { get; set; } = new();
        public List<int> SecureScoreSeries { get; set; } = new();
        public List<int> AdoptionScoreSeries { get; set; } = new();

        // Recomendaciones
        public List<(string Title, int ImpactPts)> Recommendations { get; set; } = new();

        // Tabla de amenazas recientes
        public List<ThreatItem> ThreatsTable { get; set; } = new();

        public sealed class ThreatItem
        {
            public DateTime Fecha { get; set; }
            public string Tipo { get; set; }
            public string Severidad { get; set; }
            public string Estado { get; set; }
        }
    }
}