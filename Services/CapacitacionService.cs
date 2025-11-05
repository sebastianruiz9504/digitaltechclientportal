using System;
using System.Collections.Generic;
using System.Linq;
using DigitalTechClientPortal.Models;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DigitalTechClientPortal.Services
{
    public class CapacitacionService : ICapacitacionService
    {
        private readonly ServiceClient _serviceClient;
        private readonly ILogger<CapacitacionService> _logger;

        public CapacitacionService(ServiceClient serviceClient, ILogger<CapacitacionService> logger)
        {
            _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<CapacitacionDto> ObtenerCapacitaciones()
        {
            var lista = new List<CapacitacionDto>();

            try
            {
                var query = new QueryExpression("cr07a_capacitacion")
                {
                    ColumnSet = new ColumnSet(
                        "cr07a_fecha",
                        "cr07a_duracionhoras",
                        "cr07a_cantidadasistentes",
                        "cr07a_tema"
                    ),
                    Orders = { new OrderExpression("cr07a_fecha", OrderType.Descending) }
                };

                var registros = _serviceClient.RetrieveMultiple(query).Entities;

                lista.AddRange(MapearCapacitaciones(registros));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo capacitaciones desde Dataverse");
            }

            return lista;
        }

        // 🔹 NUEVO: Método que filtra por cliente logueado
        public List<CapacitacionDto> ObtenerCapacitacionesPorCliente(Guid clienteId)
        {
            var lista = new List<CapacitacionDto>();

            try
            {
                var query = new QueryExpression("cr07a_capacitacion")
                {
                    ColumnSet = new ColumnSet(
                        "cr07a_fecha",
                        "cr07a_duracionhoras",
                        "cr07a_cantidadasistentes",
                        "cr07a_tema"
                    ),
                    Orders = { new OrderExpression("cr07a_fecha", OrderType.Descending) },
                    Criteria =
                    {
                        Filters =
                        {
                            new FilterExpression
                            {
                                Conditions =
                                {
                                    // El lookup hacia cliente en esta tabla
                                    new ConditionExpression("cr07a_cliente", ConditionOperator.Equal, clienteId)
                                }
                            }
                        }
                    }
                };

                var registros = _serviceClient.RetrieveMultiple(query).Entities;

                lista.AddRange(MapearCapacitaciones(registros));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo capacitaciones filtradas por cliente desde Dataverse");
            }

            return lista;
        }

        public Task<List<CapacitacionDto>> GetCapacitacionesAsync()
        {
            return Task.Run(() => ObtenerCapacitaciones());
        }

        public byte[]? DescargarCuestionario(Guid id)
        {
            try
            {
                var query = new QueryExpression("annotation")
                {
                    ColumnSet = new ColumnSet("documentbody", "filename", "mimetype", "createdon"),
                    Criteria = new FilterExpression
                    {
                        FilterOperator = LogicalOperator.And,
                        Conditions =
                        {
                            new ConditionExpression("objectid", ConditionOperator.Equal, id),
                            new ConditionExpression("isdocument", ConditionOperator.Equal, true)
                        }
                    },
                    Orders = { new OrderExpression("createdon", OrderType.Descending) }
                };

                var notas = _serviceClient.RetrieveMultiple(query).Entities;
                var archivo = notas.FirstOrDefault();
                if (archivo == null) return null;

                var base64 = archivo.GetAttributeValue<string>("documentbody");
                if (string.IsNullOrWhiteSpace(base64)) return null;

                return Convert.FromBase64String(base64);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error descargando cuestionario para la capacitación {CapacitacionId}", id);
                return null;
            }
        }

        // Mapeo común usado por ambos métodos
        private List<CapacitacionDto> MapearCapacitaciones(DataCollection<Entity> registros)
        {
            var lista = new List<CapacitacionDto>();

            foreach (var e in registros)
            {
                var temaValue = e.GetAttributeValue<OptionSetValue>("cr07a_tema")?.Value;
                string temaTexto = temaValue switch
                {
                    645250000 => "SharePoint o OneDrive",
                    645250001 => "Correo electrónico",
                    645250002 => "Teams",
                    645250003 => "Forms",
                    645250004 => "Seguridad",
                    645250005 => "Bookings",
                    645250006 => "Excel",
                    645250007 => "Power BI",
                    645250008 => "Otros",
                    _ => "Desconocido"
                };

                lista.Add(new CapacitacionDto
                {
                    Fecha = e.GetAttributeValue<DateTime?>("cr07a_fecha") ?? DateTime.MinValue,
                    DuracionHoras = Convert.ToDecimal(e.GetAttributeValue<int?>("cr07a_duracionhoras") ?? 0),
                    CantidadAsistentes = e.GetAttributeValue<int?>("cr07a_cantidadasistentes") ?? 0,
                    Tema = temaTexto,
                    CuestionarioUrl = $"/Capacitaciones/DescargarCuestionario/{e.Id}"
                });
            }

            return lista;
        }
    }
}