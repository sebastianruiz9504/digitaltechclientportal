using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public class CategoriaVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public class EquipoVm
    {
        public Guid Id { get; set; }
        public Guid ClienteId { get; set; }
        public Guid CategoriaId { get; set; }

        public string Marca { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
        public string Serial { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaCompra { get; set; }
        public string Ubicacion { get; set; } = string.Empty;
        public string Notas { get; set; } = string.Empty;

        public string AsignadoA { get; set; } = string.Empty;
        public Guid? UbicacionId { get; set; }
    }

    public class UserInventoryViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string UserPrincipalName { get; set; } = string.Empty;
        public string Mail { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string MobilePhone { get; set; } = string.Empty;
    }

    public class UbicacionVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
    }

    public class InventarioVm
    {
        public Guid ClienteId { get; set; }
        public List<CategoriaVm> Categorias { get; set; } = new();
        public Dictionary<Guid, List<EquipoVm>> EquiposPorCategoria { get; set; } = new();

        public List<UserInventoryViewModel> UsuariosTenant { get; set; } = new();
        public string? UsuarioSeleccionadoUpn { get; set; }
        public List<EquipoVm> EquiposDelUsuarioSeleccionado { get; set; } = new();

        public List<UbicacionVm> Ubicaciones { get; set; } = new();
        public Guid? UbicacionSeleccionadaId { get; set; }
        public List<EquipoVm> EquiposPorUbicacionSeleccionada { get; set; } = new();
    }
}