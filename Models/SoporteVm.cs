using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public class SoporteVm
    {
        public List<CloudTicketVm> CloudTickets { get; set; } = new();
        public List<CopierVm> Copiers { get; set; } = new();
    }

    public class CloudTicketVm
    {
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaCreacion { get; set; }
        public string Estado { get; set; } = "Desconocido";
        public DateTime? FechaCierre { get; set; }
    }

    public class CopierVm
    {
        public string Nombre { get; set; } = string.Empty;
        public string IdEquipo { get; set; } = string.Empty;
        public DateTime FechaMantenimiento { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string Estado { get; set; } = "Desconocido";
        public Guid ActaId { get; set; }
    }
}