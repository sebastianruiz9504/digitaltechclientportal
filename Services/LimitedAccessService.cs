using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Services
{
    /// <summary>
    /// Resuelve si el usuario está en la tabla de acceso limitado (cr07a_usuariosconaccesolimitado)
    /// y devuelve el cliente asociado (lookup cr07a_cliente).
    /// </summary>
    public sealed class LimitedAccessService
    {
        private readonly ServiceClient _svc;

        // Logical names según tu mensaje:
        // Tabla: cr07a_usuariosconaccesolimitado
        // Columnas: cr07a_name (correo), cr07a_cliente (lookup a cr07a_cliente)
        private const string LimitedTable = "cr07a_usuariosconaccesolimitado";
        private const string LimitedEmailField = "cr07a_name";
        private const string LimitedClienteLookup = "cr07a_cliente";

        public LimitedAccessService(ServiceClient svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        /// <summary>
        /// Intenta resolver el cliente para un usuario con acceso limitado (por correo).
        /// Retorna (found, clienteId, clienteNombre).
        /// </summary>
        public async Task<(bool Found, Guid ClienteId, string? ClienteNombre)> TryResolveClienteForLimitedAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return (false, Guid.Empty, null);

            var q = new QueryExpression(LimitedTable)
            {
                ColumnSet = new ColumnSet(LimitedClienteLookup)
            };
            q.Criteria.AddCondition(LimitedEmailField, ConditionOperator.Equal, email);

            var result = await _svc.RetrieveMultipleAsync(q);
            var row = result.Entities.FirstOrDefault();
            if (row == null) return (false, Guid.Empty, null);

            var clienteRef = row.GetAttributeValue<EntityReference>(LimitedClienteLookup);
            if (clienteRef == null || clienteRef.Id == Guid.Empty) return (false, Guid.Empty, null);

            // Intentar obtener un nombre legible del cliente (si no viene en el formatted)
            var nombre = clienteRef.Name;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                try
                {
                    var cliente = await _svc.RetrieveAsync("cr07a_cliente", clienteRef.Id, new ColumnSet("cr07a_name", "cr07a_nombre"));
                    nombre = cliente.GetAttributeValue<string>("cr07a_nombre")
                             ?? cliente.GetAttributeValue<string>("cr07a_name");
                }
                catch { /* silencioso */ }
            }

            return (true, clienteRef.Id, nombre);
        }
    }
}
