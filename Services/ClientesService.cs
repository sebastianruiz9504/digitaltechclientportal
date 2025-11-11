using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Services
{
    public sealed class ClientesService
    {
        private readonly ServiceClient _svc;
        private readonly LimitedAccessService _limited;

        public ClientesService(ServiceClient svc, LimitedAccessService limited)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _limited = limited ?? throw new ArgumentNullException(nameof(limited));
        }

        /// <summary>
        /// Busca el cliente por correo en cr07a_cliente (columna cr07a_correoelectronico).
        /// Si no lo encuentra, busca en cr07a_usuariosconaccesolimitado (cr07a_name) y devuelve el lookup cr07a_cliente.
        /// </summary>
        public async Task<Guid> GetClienteIdByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Guid.Empty;

            // 1) Intento directo en cr07a_cliente
            //    (ajusta el logical name del campo de email si difiere)
            var q = new QueryExpression("cr07a_cliente")
            {
                ColumnSet = new ColumnSet("cr07a_clienteid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("cr07a_correoelectronico", ConditionOperator.Equal, email)
                    }
                }
            };

            var result = await _svc.RetrieveMultipleAsync(q);
            var entity = result.Entities.FirstOrDefault();
            if (entity != null)
                return entity.Id;

            // 2) Fallback: usuarios con acceso limitado
            var limited = await _limited.TryResolveClienteForLimitedAsync(email);
            if (limited.Found)
                return limited.ClienteId;

            return Guid.Empty;
        }

        /// <summary>
        /// Devuelve el nombre del cliente (útil si necesitas pintar el nombre en layout).
        /// Intenta primero por cliente “normal”; si no, por acceso limitado.
        /// </summary>
        public async Task<string?> GetClienteNombreByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            // 1) Cliente normal (lee cualquier campo de nombre que tengas)
            var q = new QueryExpression("cr07a_cliente")
            {
                ColumnSet = new ColumnSet("cr07a_clienteid", "cr07a_nombre", "cr07a_name"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("cr07a_correoelectronico", ConditionOperator.Equal, email)
                    }
                }
            };

            var result = await _svc.RetrieveMultipleAsync(q);
            var e = result.Entities.FirstOrDefault();
            if (e != null)
            {
                var nombre = e.GetAttributeValue<string>("cr07a_nombre") ?? e.GetAttributeValue<string>("cr07a_name");
                return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
            }

            // 2) Limitado
            var limited = await _limited.TryResolveClienteForLimitedAsync(email);
            if (limited.Found) return limited.ClienteNombre;

            return null;
        }
    }
}
