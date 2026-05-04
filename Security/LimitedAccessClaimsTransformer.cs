using Microsoft.AspNetCore.Authentication;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Services
{
    /// <summary>
    /// Agrega claims al principal autenticado:
    /// - dt_limited = "1" si el usuario está en cr07a_usuariosconaccesolimitado
    /// - dt_client_id, dt_client_name (si se pudo resolver)
    /// </summary>
    public sealed class LimitedAccessClaimsTransformer : IClaimsTransformation
    {
        private readonly PortalPermissionService _permissions;

        public LimitedAccessClaimsTransformer(PortalPermissionService permissions)
        {
            _permissions = permissions;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = principal.Identity as ClaimsIdentity;
            if (identity == null || !identity.IsAuthenticated)
                return principal;

            // No recalcular si ya existe
            if (identity.HasClaim(c => c.Type == "dt_permission_checked"))
                return principal;

            var email = principal.FindFirst("preferred_username")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value
                        ?? principal.FindFirst("upn")?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return principal;

            var access = await _permissions.GetAccessForEmailAsync(email);
            identity.AddClaim(new Claim("dt_permission_checked", "1"));

            if (access.IsPrincipal)
            {
                identity.AddClaim(new Claim("dt_client_admin", "1"));
                identity.AddClaim(new Claim("dt_client_id", access.ClienteId.ToString()));
                if (!string.IsNullOrWhiteSpace(access.ClienteNombre))
                    identity.AddClaim(new Claim("dt_client_name", access.ClienteNombre));
            }

            if (access.IsLimited)
            {
                identity.AddClaim(new Claim("dt_limited", "1"));
                identity.AddClaim(new Claim("dt_limited_active", access.IsActive ? "1" : "0"));
                identity.AddClaim(new Claim("dt_client_id", access.ClienteId.ToString()));
                identity.AddClaim(new Claim("dt_allowed_modules", string.Join(";", access.AllowedModules.OrderBy(x => x))));
                if (!string.IsNullOrWhiteSpace(access.ClienteNombre))
                    identity.AddClaim(new Claim("dt_client_name", access.ClienteNombre));
            }

            return principal;
        }
    }
}
