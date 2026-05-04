using System;
using System.Collections.Generic;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Models
{
    public sealed class PermisosIndexVm
    {
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public bool PermissionColumnsAvailable { get; set; } = true;
        public IReadOnlyList<PortalModuleDefinition> Modulos { get; set; } = PortalModuleKeys.All;
        public List<UsuarioPermisoVm> Usuarios { get; set; } = new();
        public PermisoEditVm Nuevo { get; set; } = new();
    }

    public sealed class UsuarioPermisoVm
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public List<string> ModulosSeleccionados { get; set; } = new();
    }

    public sealed class PermisoEditVm
    {
        public Guid? Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public bool Activo { get; set; } = true;
        public List<string> ModulosSeleccionados { get; set; } = new();
    }
}
