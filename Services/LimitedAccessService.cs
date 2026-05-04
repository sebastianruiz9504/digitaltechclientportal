using System;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Services
{
    /// <summary>
    /// Resuelve si el usuario está en la tabla de acceso limitado (cr07a_usuariosconaccesolimitado)
    /// y devuelve el cliente asociado (lookup cr07a_cliente).
    /// </summary>
    public sealed class LimitedAccessService
    {
        private readonly PortalPermissionService _permissions;

        public LimitedAccessService(PortalPermissionService permissions)
        {
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        }

        /// <summary>
        /// Intenta resolver el cliente para un usuario con acceso limitado (por correo).
        /// Retorna (found, clienteId, clienteNombre).
        /// </summary>
        public async Task<(bool Found, Guid ClienteId, string? ClienteNombre)> TryResolveClienteForLimitedAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return (false, Guid.Empty, null);

            return await _permissions.TryResolveClienteForLimitedAsync(email);
        }
    }
}
