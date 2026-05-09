using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace DigitalTechClientPortal.Web.Models
{
    public sealed class LicenciamientoViewModel
    {
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public DateTime FechaCorte { get; set; }
        public int DiaCorte { get; set; } = 15;
        public int MesSeleccionado { get; set; }
        public int AnioSeleccionado { get; set; }
        public int DiasMes { get; set; } = 30;
        public bool PuedeCambiarCliente { get; set; }
        public bool PuedeEditarEstructura { get; set; }
        public Guid? ClienteSeleccionadoId { get; set; }
        public string? Mensaje { get; set; }
        public string? Error { get; set; }
        public List<ClienteLookupVm> ClientesDisponibles { get; set; } = new();
        public List<LicenciaProductoResumenVm> ProductosRazonPadre { get; set; } = new();
        public List<SubRazonSocialVm> SubRazones { get; set; } = new();
        public List<SolicitudLicenciaVm> HistoricoSolicitudes { get; set; } = new();

        public decimal TotalPrefactura => SubRazones.Sum(x => x.TotalPrefactura);
        public int TotalLicenciasPadre => ProductosRazonPadre.Sum(x => x.CantidadTotal);
        public int TotalLicenciasAsignadas => ProductosRazonPadre.Sum(x => x.CantidadAsignada);
        public int TotalLicenciasSinAsignar => ProductosRazonPadre.Sum(x => Math.Max(0, x.CantidadSinAsignar));
    }

    public sealed class ClienteLookupVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public sealed class LicenciaProductoResumenVm
    {
        public Guid SalesRecordId { get; set; }
        public Guid? ProductoId { get; set; }
        public string ProductoLogicalName { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public int CantidadTotal { get; set; }
        public int CantidadAsignada { get; set; }
        public int CantidadSinAsignar => CantidadTotal - CantidadAsignada;
        public decimal PrecioUnitarioUsd { get; set; }
        public int? DiaFacturacion { get; set; }
    }

    public sealed class SubRazonSocialVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public List<ConsumoLicenciaVm> Consumo { get; set; } = new();
        public decimal TotalPrefactura => Consumo.Sum(c => c.TotalProrrateadoUsd);
        public int TotalLicencias => Consumo.Where(c => !c.EsProrrateoSolicitud).Sum(c => c.Cantidad);
    }

    public sealed class ConsumoLicenciaVm
    {
        public Guid SalesRecordId { get; set; }
        public Guid? SolicitudId { get; set; }
        public string Producto { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public int DiasConsumo { get; set; }
        public int DiasMes { get; set; } = 30;
        public decimal PrecioUnitarioUsd { get; set; }
        public bool EsProrrateoSolicitud { get; set; }
        public string Origen { get; set; } = "Base mensual";
        public decimal TotalProrrateadoUsd => DiasMes <= 0
            ? 0
            : Math.Round((Cantidad * PrecioUnitarioUsd) * (DiasConsumo / (decimal)DiasMes), 2);
    }

    public sealed class SolicitudLicenciaVm
    {
        public Guid Id { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public DateTime? FechaProrrateo { get; set; }
        public string SolicitadoPor { get; set; } = string.Empty;
        public string SubRazon { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public int CantidadNueva { get; set; }
        public decimal PrecioUnitarioUsd { get; set; }
        public string Estado { get; set; } = "Pendiente";
    }

    public sealed class CrearSubRazonLicenciamientoVm
    {
        public Guid ClienteId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Required(ErrorMessage = "Escribe el nombre de la subrazón social.")]
        [StringLength(200)]
        public string Nombre { get; set; } = string.Empty;
    }

    public sealed class GuardarAsignacionLicenciamientoVm
    {
        public Guid ClienteId { get; set; }
        public Guid SubRazonId { get; set; }
        public Guid SalesRecordId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Range(0, 1000000, ErrorMessage = "La cantidad debe ser mayor o igual a cero.")]
        public int Cantidad { get; set; }
    }

    public sealed class SolicitarLicenciasVm
    {
        public Guid ClienteId { get; set; }
        public Guid SubRazonId { get; set; }
        public Guid SalesRecordId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Range(1, 1000000, ErrorMessage = "Solo puedes solicitar aumentos de una licencia o más.")]
        public int Cantidad { get; set; }
    }

    public sealed class ActualizarFechaCorteLicenciamientoVm
    {
        public Guid ClienteId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Range(1, 31, ErrorMessage = "El día de facturación debe estar entre 1 y 31.")]
        public int DiaCorte { get; set; }
    }
}
