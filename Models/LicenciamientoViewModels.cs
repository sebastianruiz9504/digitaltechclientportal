using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalTechClientPortal.Web.Models
{
    public sealed class LicenciamientoViewModel
    {
        public string ClienteNombre { get; set; } = string.Empty;
        public DateTime FechaCorte { get; set; }
        public bool PuedeCambiarCliente { get; set; }
        public string? ClienteSeleccionadoId { get; set; }
        public List<ClienteLookupVm> ClientesDisponibles { get; set; } = new();
        public List<LicenciaProductoResumenVm> ProductosRazonPadre { get; set; } = new();
        public List<SubRazonSocialVm> SubRazones { get; set; } = new();
        public List<SolicitudLicenciaVm> HistoricoSolicitudes { get; set; } = new();

        public decimal TotalPrefactura => SubRazones.Sum(x => x.TotalPrefactura);
    }

    public sealed class ClienteLookupVm { public string Id { get; set; } = string.Empty; public string Nombre { get; set; } = string.Empty; }

    public sealed class LicenciaProductoResumenVm
    {
        public string Producto { get; set; } = string.Empty;
        public int CantidadTotal { get; set; }
        public decimal PrecioUnitarioUsd { get; set; }
    }

    public sealed class SubRazonSocialVm
    {
        public string Nombre { get; set; } = string.Empty;
        public List<ConsumoLicenciaVm> Consumo { get; set; } = new();
        public decimal TotalPrefactura => Consumo.Sum(c => c.TotalProrrateadoUsd);
    }

    public sealed class ConsumoLicenciaVm
    {
        public string Producto { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int DiasConsumo { get; set; }
        public decimal PrecioUnitarioUsd { get; set; }
        public decimal TotalProrrateadoUsd => Math.Round((Cantidad * PrecioUnitarioUsd) * (DiasConsumo / 30m), 2);
    }

    public sealed class SolicitudLicenciaVm
    {
        public DateTime FechaSolicitud { get; set; }
        public string SolicitadoPor { get; set; } = string.Empty;
        public string SubRazon { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public int CantidadNueva { get; set; }
        public string Estado { get; set; } = "Pendiente";
    }
}
