using System;

namespace DigitalTechClientPortal.Models
{
    public sealed class ReporteCloudDto
    {
        public Guid Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public Guid AdjuntoId { get; set; } // ID del archivo en Dataverse
    }
}       