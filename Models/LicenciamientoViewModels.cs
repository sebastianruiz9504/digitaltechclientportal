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
        public string GrupoEmpresarialId { get; set; } = string.Empty;
        public string GrupoEmpresarialNombre { get; set; } = string.Empty;
        public bool TieneGrupoEmpresarial { get; set; }
        public DateTime FechaCorte { get; set; }
        public int DiaCorte { get; set; } = 15;
        public int MesSeleccionado { get; set; }
        public int AnioSeleccionado { get; set; }
        public int DiasMes { get; set; } = 30;
        public bool PuedeCambiarCliente { get; set; }
        public bool PuedeEditarEstructura { get; set; }
        public bool PuedeMoverCantidades { get; set; }
        public Guid? ClienteSeleccionadoId { get; set; }
        public string? Mensaje { get; set; }
        public string? Error { get; set; }
        public List<ClienteLookupVm> ClientesDisponibles { get; set; } = new();
        public List<AccountIdLicenciamientoVm> AccountIdsDisponibles { get; set; } = new();
        public List<AccountIdLicenciamientoVm> AccountIdsGrupoActual { get; set; } = new();
        public List<LicenciaProductoResumenVm> ProductosRazonPadre { get; set; } = new();
        public List<ClienteLicenciamientoVm> ClientesHijos { get; set; } = new();
        public List<SolicitudLicenciaVm> HistoricoSolicitudes { get; set; } = new();

        public string RazonPadreNombre => string.IsNullOrWhiteSpace(GrupoEmpresarialNombre)
            ? ClienteNombre
            : GrupoEmpresarialNombre;

        public decimal TotalPrefactura => ClientesHijos.Sum(x => x.TotalPrefactura);
        public int TotalLicenciasPadre => ProductosRazonPadre.Sum(x => x.CantidadTotal);
        public int TotalLicenciasAsignadas => ClientesHijos.Sum(x => x.TotalLicencias);
        public int TotalLicenciasSinAsignar => 0;
        public int TotalClientesHijos => ClientesHijos.Count;
        public int TotalAccountIds => ClientesHijos.Sum(c => c.AccountIds.Count);
    }

    public sealed class ClienteLookupVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public sealed class AccountIdLicenciamientoVm
    {
        public Guid Id { get; set; }
        public string AccountId { get; set; } = string.Empty;
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string GrupoEmpresarialId { get; set; } = string.Empty;
        public string GrupoEmpresarialNombre { get; set; } = string.Empty;

        public string Display => string.IsNullOrWhiteSpace(AccountId)
            ? ClienteNombre
            : $"{AccountId} - {ClienteNombre}";
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
        public List<string> ClientesConProducto { get; set; } = new();
        public List<string> AccountIds { get; set; } = new();
    }

    public sealed class ClienteLicenciamientoVm
    {
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public List<string> AccountIds { get; set; } = new();
        public List<ConsumoLicenciaVm> Consumo { get; set; } = new();

        public decimal TotalPrefactura => Consumo.Sum(c => c.TotalProrrateadoUsd);
        public int TotalLicencias => Consumo.Where(c => !c.EsProrrateoSolicitud).Sum(c => c.Cantidad);
        public int TotalProductos => Consumo.Where(c => !c.EsProrrateoSolicitud).Select(c => c.SalesRecordId).Distinct().Count();
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
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public Guid? ProductoId { get; set; }
        public string ProductoLogicalName { get; set; } = string.Empty;
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
        public string ClienteHijo { get; set; } = string.Empty;
        public string SubRazon { get; set; } = string.Empty;
        public string GrupoEmpresarial { get; set; } = string.Empty;
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
        public Guid ClienteHijoId { get; set; }
        public Guid SubRazonId { get; set; }
        public Guid SalesRecordId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Range(1, 1000000, ErrorMessage = "Solo puedes solicitar aumentos de una licencia o más.")]
        public int Cantidad { get; set; }
    }

    public sealed class MoverLicenciasVm
    {
        public Guid ClienteId { get; set; }
        public Guid OrigenClienteId { get; set; }
        public Guid DestinoClienteId { get; set; }
        public Guid OrigenSalesRecordId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [Range(1, 1000000, ErrorMessage = "La cantidad a mover debe ser mayor a cero.")]
        public int Cantidad { get; set; }
    }

    public sealed class ActualizarGrupoEmpresarialVm
    {
        public Guid ClienteId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }

        [StringLength(100)]
        public string GrupoEmpresarialId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Escribe el nombre del grupo empresarial.")]
        [StringLength(200)]
        public string GrupoEmpresarialNombre { get; set; } = string.Empty;
    }

    public sealed class AsignarAccountIdGrupoVm
    {
        public Guid ClienteId { get; set; }
        public Guid AccountIdRowId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
        public string GrupoEmpresarialId { get; set; } = string.Empty;
        public string GrupoEmpresarialNombre { get; set; } = string.Empty;
    }

    public sealed class QuitarAccountIdGrupoVm
    {
        public Guid ClienteId { get; set; }
        public Guid AccountIdRowId { get; set; }
        public int Mes { get; set; }
        public int Anio { get; set; }
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
