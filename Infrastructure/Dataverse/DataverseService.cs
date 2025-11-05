using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using DigitalTechClientPortal.Domain.Dataverse;

namespace DigitalTechClientPortal.Infrastructure.Dataverse
{
    public interface IDataverseService
    {
        IReadOnlyList<FacturaDto> GetTodasLasFacturas();
        IReadOnlyList<FacturaDto> GetFacturasPorCliente(Guid clienteId); // <-- Nuevo
    }

    public sealed class DataverseService : IDataverseService
    {
        private readonly IOrganizationService _svc;

        public DataverseService(IOrganizationService svc)
            => _svc = svc ?? throw new ArgumentNullException(nameof(svc));

        public IReadOnlyList<FacturaDto> GetTodasLasFacturas()
        {
            var qe = new QueryExpression(Cr07aSchema.FacturacionEntity)
            {
                ColumnSet = new ColumnSet(
                    Cr07aSchema.FacturacionId,
                    Cr07aSchema.Facturacion_Numero,
                    Cr07aSchema.Facturacion_Fecha,
                    Cr07aSchema.Facturacion_Monto,
                    Cr07aSchema.Facturacion_Estado,
                    Cr07aSchema.Facturacion_ClienteLookup,
                    Cr07aSchema.Facturacion_PublicUrl
                ),
                PageInfo = new PagingInfo
                {
                    Count = 500,
                    PageNumber = 1
                }
            };

            qe.Orders.Add(new OrderExpression(
                Cr07aSchema.Facturacion_DefaultOrderDesc,
                OrderType.Descending));

            var resp = ExecuteWithRetry(() => _svc.RetrieveMultiple(qe));
            return resp.Entities.Select(MapFactura).ToList();
        }

        // Nuevo método para filtrar facturas por cliente
        public IReadOnlyList<FacturaDto> GetFacturasPorCliente(Guid clienteId)
        {
            var qe = new QueryExpression(Cr07aSchema.FacturacionEntity)
            {
                ColumnSet = new ColumnSet(
                    Cr07aSchema.FacturacionId,
                    Cr07aSchema.Facturacion_Numero,
                    Cr07aSchema.Facturacion_Fecha,
                    Cr07aSchema.Facturacion_Monto,
                    Cr07aSchema.Facturacion_Estado,
                    Cr07aSchema.Facturacion_ClienteLookup,
                    Cr07aSchema.Facturacion_PublicUrl
                ),
                PageInfo = new PagingInfo
                {
                    Count = 500,
                    PageNumber = 1
                },
                Criteria =
                {
                    Filters =
                    {
                        new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression(
                                    Cr07aSchema.Facturacion_ClienteLookup,
                                    ConditionOperator.Equal,
                                    clienteId)
                            }
                        }
                    }
                }
            };

            qe.Orders.Add(new OrderExpression(
                Cr07aSchema.Facturacion_DefaultOrderDesc,
                OrderType.Descending));

            var resp = ExecuteWithRetry(() => _svc.RetrieveMultiple(qe));
            return resp.Entities.Select(MapFactura).ToList();
        }

        private static FacturaDto MapFactura(Entity e)
        {
            var formatted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in e.FormattedValues) formatted[kv.Key] = kv.Value;

            var montoRaw = e[Cr07aSchema.Facturacion_Monto];
            decimal? monto = montoRaw switch
            {
                Money m => m.Value,
                decimal d => d,
                _ => null
            };
            var estado = e.GetAttributeValue<OptionSetValue>(Cr07aSchema.Facturacion_Estado);

            return new FacturaDto
            {
                Id = e.Id,
                Numero = e.GetAttributeValue<string>(Cr07aSchema.Facturacion_Numero),
                Fecha = e.GetAttributeValue<DateTime?>(Cr07aSchema.Facturacion_Fecha),
                Monto = monto,
                MontoFormatted = formatted.TryGetValue(Cr07aSchema.Facturacion_Monto, out var mf) ? mf : null,
                EstadoValue = estado?.Value,
                EstadoLabel = formatted.TryGetValue(Cr07aSchema.Facturacion_Estado, out var el) ? el : null,
                ClienteId = e.GetAttributeValue<EntityReference>(Cr07aSchema.Facturacion_ClienteLookup)?.Id,
                PublicUrl = e.GetAttributeValue<string>(Cr07aSchema.Facturacion_PublicUrl),
                FormattedValues = formatted
            };
        }

        private static T ExecuteWithRetry<T>(Func<T> action, int maxRetries = 3, int baseDelayMs = 200)
        {
            var attempt = 0;
            while (true)
            {
                try
                {
                    return action();
                }
                catch (FaultException<OrganizationServiceFault>) when (attempt < maxRetries)
                {
                    attempt++;
                    Thread.Sleep(baseDelayMs * attempt);
                }
                catch (TimeoutException) when (attempt < maxRetries)
                {
                    attempt++;
                    Thread.Sleep(baseDelayMs * attempt);
                }
            }
        }
    }
}