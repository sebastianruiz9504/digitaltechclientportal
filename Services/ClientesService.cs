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

        public ClientesService(ServiceClient svc)
        {
            _svc = svc;
        }

        public async Task<Guid> GetClienteIdByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Guid.Empty;

            // Ajusta el logical name del campo de email si es distinto
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
            return entity?.Id ?? Guid.Empty;
        }
    }
}