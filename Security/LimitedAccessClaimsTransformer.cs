using Microsoft.AspNetCore.Authentication;
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
        private readonly LimitedAccessService _limited;

        public LimitedAccessClaimsTransformer(LimitedAccessService limited)
        {
            _limited = limited;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = principal.Identity as ClaimsIdentity;
            if (identity == null || !identity.IsAuthenticated)
                return principal;

            // No recalcular si ya existe
            if (identity.HasClaim(c => c.Type == "dt_limited"))
                return principal;

            var email = principal.FindFirst("preferred_username")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value
                        ?? principal.FindFirst("upn")?.Value;

            if (string.IsNullOrWhiteSpace(email))
                return principal;

            var limited = await _limited.TryResolveClienteForLimitedAsync(email);
            if (limited.Found)
            {
                identity.AddClaim(new Claim("dt_limited", "1"));
                identity.AddClaim(new Claim("dt_client_id", limited.ClienteId.ToString()));
                if (!string.IsNullOrWhiteSpace(limited.ClienteNombre))
                    identity.AddClaim(new Claim("dt_client_name", limited.ClienteNombre));
            }

            return principal;
        }
    }
}
