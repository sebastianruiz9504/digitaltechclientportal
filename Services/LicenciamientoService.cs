using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using DigitalTechClientPortal.Web.Models;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DigitalTechClientPortal.Services
{
    public sealed class LicenciamientoService
    {
        private const string AdminLicenciamientoEmail = "sruiz@digitaltechcolombia.com";

        private const string ClienteEntity = "cr07a_cliente";
        private const string ClienteNombre = "cr07a_nombre";

        private const string ProductoCloudEntity = "cr07a_salesperformancerecord";
        private const string ProductoCloudId = "cr07a_salesperformancerecordid";
        private const string ProductoCloudCliente = "cr07a_clientelookup";
        private const string ProductoCloudProducto = "cr07a_producto";
        private const string ProductoCloudNombre = "cr07a_productname";
        private const string ProductoCloudCantidad = "cr07a_quantity";
        private const string ProductoCloudPrecioDecimal = "cr07a_valorventaunidadusd";
        private const string ProductoCloudPrecioMoney = "cr07a_unitsalevalueusd";
        private const string ProductoCloudDiaFacturacion = "cr07a_billingday";

        private const string SolicitudEntity = "cr07a_solicitudaprovisionamiento";
        private const string SolicitudCliente = "cr07a_cliente";
        private const string SolicitudProducto = "cr07a_producto";
        private const string SolicitudCantidad = "cr07a_cantidad";
        private const string SolicitudEstado = "cr07a_estado";
        private const string SolicitudFecha = "cr07a_fechadeaprovisionamiento";
        private const string SolicitudFechaProrrateo = "cr07a_fechaprorateosiaplica";
        private const string SolicitudNombre = "cr07a_name";
        private const string SolicitudDetalle = "cr07a_productoycantidades";
        private const string SolicitudValorUnitario = "cr07a_valorunitario";
        private const string SolicitudSubRazon = "cr07a_subrazonlicenciamiento";
        private const string SolicitudRegistroProductoCloud = "cr07a_registroproductocloud";
        private const string SolicitudSolicitadoPor = "cr07a_solicitadopor";

        private const string SubRazonEntity = "cr07a_subrazonsociallicenciamiento";
        private const string SubRazonNombre = "cr07a_name";
        private const string SubRazonCliente = "cr07a_cliente";

        private const string AsignacionEntity = "cr07a_asignacionlicenciamiento";
        private const string AsignacionNombre = "cr07a_name";
        private const string AsignacionCliente = "cr07a_cliente";
        private const string AsignacionSubRazon = "cr07a_subrazon";
        private const string AsignacionProductoCloud = "cr07a_registroproductocloud";
        private const string AsignacionCantidad = "cr07a_cantidad";

        private const int SolicitudEstadoPendiente = 645250000;
        private const int SolicitudEstadoAprovisionado = 645250001;
        private const int SolicitudEstadoAprobado = 645250002;
        private const int LabelLanguage = 1033;

        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static LicenciamientoSchemaStatus? CachedSchemaStatus;
        private static bool MetadataTouchedDuringEnsure;

        private readonly ServiceClient _svc;
        private readonly ClientesService _clientesService;
        private readonly ILogger<LicenciamientoService> _logger;

        public LicenciamientoService(ServiceClient svc, ClientesService clientesService, ILogger<LicenciamientoService> logger)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _clientesService = clientesService ?? throw new ArgumentNullException(nameof(clientesService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<LicenciamientoViewModel> BuildViewModelAsync(
            IReadOnlyList<string> candidateEmails,
            Guid? requestedClientId,
            int? mes,
            int? anio,
            string? mensaje = null,
            string? error = null)
        {
            var now = DateTime.Today;
            var selectedMonth = mes.GetValueOrDefault(now.Month);
            var selectedYear = anio.GetValueOrDefault(now.Year);

            if (selectedMonth is < 1 or > 12)
            {
                selectedMonth = now.Month;
            }

            if (selectedYear is < 2000 or > 2100)
            {
                selectedYear = now.Year;
            }

            var schema = await EnsureSchemaAsync();
            var canSwitchClient = IsAdminLicenciamiento(candidateEmails);
            var clientes = canSwitchClient ? await GetClientesAsync() : new List<ClienteLookupVm>();
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, requestedClientId, clientes);

            if (clienteId == Guid.Empty)
            {
                return new LicenciamientoViewModel
                {
                    MesSeleccionado = selectedMonth,
                    AnioSeleccionado = selectedYear,
                    DiasMes = DateTime.DaysInMonth(selectedYear, selectedMonth),
                    PuedeCambiarCliente = canSwitchClient,
                    PuedeEditarEstructura = canSwitchClient,
                    ClientesDisponibles = clientes,
                    Error = "No encontré un cliente asociado al usuario autenticado."
                };
            }

            var clienteNombre = await GetClienteNombreAsync(clienteId);
            if (canSwitchClient && clientes.All(c => c.Id != clienteId))
            {
                clientes.Add(new ClienteLookupVm { Id = clienteId, Nombre = clienteNombre });
                clientes = clientes.OrderBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var diasMes = DateTime.DaysInMonth(selectedYear, selectedMonth);
            var productos = await GetProductosCloudAsync(clienteId);
            var subRazones = schema.SubRazonReady
                ? await GetSubRazonesAsync(clienteId)
                : new List<SubRazonDto>();
            var asignaciones = schema.AsignacionReady
                ? await GetAsignacionesAsync(clienteId)
                : new List<AsignacionDto>();

            var productosPorId = productos.ToDictionary(p => p.SalesRecordId);
            var subRazonesPorId = subRazones.ToDictionary(s => s.Id);
            var solicitudes = await GetSolicitudesAsync(clienteId, schema, productosPorId, subRazonesPorId);

            var productosVm = productos
                .Select(p => new LicenciaProductoResumenVm
                {
                    SalesRecordId = p.SalesRecordId,
                    ProductoId = p.ProductoReference?.Id,
                    ProductoLogicalName = p.ProductoReference?.LogicalName ?? string.Empty,
                    Producto = p.Nombre,
                    CantidadTotal = p.Cantidad,
                    CantidadAsignada = asignaciones
                        .Where(a => a.SalesRecordId == p.SalesRecordId)
                        .Sum(a => a.Cantidad),
                    PrecioUnitarioUsd = p.PrecioUnitarioUsd,
                    DiaFacturacion = p.DiaFacturacion
                })
                .OrderBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var subRazonesVm = subRazones
                .OrderBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SubRazonSocialVm
                {
                    Id = s.Id,
                    Nombre = s.Nombre,
                    Consumo = asignaciones
                        .Where(a => a.SubRazonId == s.Id && productosPorId.ContainsKey(a.SalesRecordId) && a.Cantidad > 0)
                        .Select(a =>
                        {
                            var producto = productosPorId[a.SalesRecordId];
                            return new ConsumoLicenciaVm
                            {
                                SalesRecordId = producto.SalesRecordId,
                                Producto = producto.Nombre,
                                Cantidad = a.Cantidad,
                                DiasConsumo = diasMes,
                                DiasMes = diasMes,
                                PrecioUnitarioUsd = producto.PrecioUnitarioUsd,
                                Origen = "Base mensual"
                            };
                        })
                        .OrderBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();

            foreach (var solicitud in solicitudes.Where(s => IsSolicitudFacturable(s.EstadoValor)))
            {
                if (!solicitud.SubRazonId.HasValue ||
                    !productosPorId.TryGetValue(solicitud.SalesRecordId.GetValueOrDefault(), out var producto))
                {
                    continue;
                }

                var subRazonVm = subRazonesVm.FirstOrDefault(s => s.Id == solicitud.SubRazonId.Value);
                if (subRazonVm == null)
                {
                    continue;
                }

                var fechaBase = (solicitud.FechaProrrateo ?? solicitud.FechaSolicitud ?? new DateTime(selectedYear, selectedMonth, 1)).Date;
                if (fechaBase.Year != selectedYear || fechaBase.Month != selectedMonth)
                {
                    continue;
                }

                subRazonVm.Consumo.Add(new ConsumoLicenciaVm
                {
                    SalesRecordId = producto.SalesRecordId,
                    SolicitudId = solicitud.Id,
                    Producto = producto.Nombre,
                    Cantidad = solicitud.Cantidad,
                    DiasConsumo = Math.Max(1, diasMes - fechaBase.Day + 1),
                    DiasMes = diasMes,
                    PrecioUnitarioUsd = solicitud.PrecioUnitarioUsd > 0 ? solicitud.PrecioUnitarioUsd : producto.PrecioUnitarioUsd,
                    EsProrrateoSolicitud = true,
                    Origen = $"Prorrateo desde {fechaBase:dd/MM/yyyy}"
                });
            }

            var diaCorte = productos.FirstOrDefault(p => p.DiaFacturacion.HasValue)?.DiaFacturacion ?? 15;
            var fechaCorte = new DateTime(selectedYear, selectedMonth, Math.Min(diaCorte, diasMes));

            return new LicenciamientoViewModel
            {
                ClienteId = clienteId,
                ClienteNombre = clienteNombre,
                FechaCorte = fechaCorte,
                DiaCorte = diaCorte,
                MesSeleccionado = selectedMonth,
                AnioSeleccionado = selectedYear,
                DiasMes = diasMes,
                PuedeCambiarCliente = canSwitchClient,
                PuedeEditarEstructura = true,
                ClienteSeleccionadoId = clienteId,
                ClientesDisponibles = clientes,
                ProductosRazonPadre = productosVm,
                SubRazones = subRazonesVm,
                HistoricoSolicitudes = solicitudes
                    .Select(s => new SolicitudLicenciaVm
                    {
                        Id = s.Id,
                        FechaSolicitud = s.FechaSolicitud,
                        FechaProrrateo = s.FechaProrrateo,
                        SolicitadoPor = s.SolicitadoPor,
                        SubRazon = s.SubRazonNombre,
                        Producto = s.ProductoNombre,
                        CantidadNueva = s.Cantidad,
                        PrecioUnitarioUsd = s.PrecioUnitarioUsd,
                        Estado = EstadoSolicitudLabel(s.EstadoValor)
                    })
                    .ToList(),
                Mensaje = mensaje,
                Error = error ?? schema.SetupError
            };
        }

        public async Task<Guid> CrearSubRazonAsync(IReadOnlyList<string> candidateEmails, CrearSubRazonLicenciamientoVm input)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para crear subrazones.");
            }

            var schema = await EnsureSchemaAsync();
            if (!schema.SubRazonReady)
            {
                throw new InvalidOperationException("La tabla de subrazones de licenciamiento no está disponible en Dataverse.");
            }

            var nombre = (input.Nombre ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new InvalidOperationException("Escribe el nombre de la subrazón social.");
            }

            var existing = await GetSubRazonByNameAsync(clienteId, nombre);
            if (existing.HasValue)
            {
                return clienteId;
            }

            var entity = new Entity(SubRazonEntity)
            {
                [SubRazonNombre] = nombre,
                [SubRazonCliente] = new EntityReference(ClienteEntity, clienteId)
            };

            await _svc.CreateAsync(entity);
            return clienteId;
        }

        public async Task<Guid> GuardarAsignacionAsync(IReadOnlyList<string> candidateEmails, GuardarAsignacionLicenciamientoVm input)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para modificar asignaciones.");
            }

            var schema = await EnsureSchemaAsync();
            if (!schema.SubRazonReady || !schema.AsignacionReady)
            {
                throw new InvalidOperationException("Las tablas de subrazones/asignaciones de licenciamiento no están disponibles en Dataverse.");
            }

            var producto = await GetProductoCloudAsync(input.SalesRecordId, clienteId);
            var subRazon = await GetSubRazonAsync(input.SubRazonId, clienteId);
            if (producto == null)
            {
                throw new InvalidOperationException("El producto seleccionado no pertenece al cliente actual.");
            }

            if (subRazon == null)
            {
                throw new InvalidOperationException("La subrazón seleccionada no pertenece al cliente actual.");
            }

            if (input.Cantidad < 0)
            {
                throw new InvalidOperationException("La cantidad asignada no puede ser negativa.");
            }

            var existing = await GetAsignacionAsync(clienteId, input.SubRazonId, input.SalesRecordId);
            var totalOtros = await GetTotalAsignadoProductoAsync(clienteId, input.SalesRecordId, existing?.Id);
            if (totalOtros + input.Cantidad > producto.Cantidad)
            {
                throw new InvalidOperationException(
                    $"La asignación supera las {producto.Cantidad} licencias disponibles para {producto.Nombre}.");
            }

            if (existing == null)
            {
                var entity = new Entity(AsignacionEntity)
                {
                    [AsignacionNombre] = $"{subRazon.Nombre} - {producto.Nombre}",
                    [AsignacionCliente] = new EntityReference(ClienteEntity, clienteId),
                    [AsignacionSubRazon] = new EntityReference(SubRazonEntity, subRazon.Id),
                    [AsignacionProductoCloud] = new EntityReference(ProductoCloudEntity, producto.SalesRecordId),
                    [AsignacionCantidad] = input.Cantidad
                };

                await _svc.CreateAsync(entity);
            }
            else
            {
                var entity = new Entity(AsignacionEntity, existing.Id)
                {
                    [AsignacionNombre] = $"{subRazon.Nombre} - {producto.Nombre}",
                    [AsignacionCantidad] = input.Cantidad
                };

                await _svc.UpdateAsync(entity);
            }

            return clienteId;
        }

        public async Task<Guid> SolicitarLicenciasAsync(
            IReadOnlyList<string> candidateEmails,
            SolicitarLicenciasVm input,
            string solicitante)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para solicitar licencias.");
            }

            if (input.Cantidad <= 0)
            {
                throw new InvalidOperationException("Solo se permiten solicitudes para subir licencias.");
            }

            var schema = await EnsureSchemaAsync();
            var producto = await GetProductoCloudAsync(input.SalesRecordId, clienteId);
            var subRazon = await GetSubRazonAsync(input.SubRazonId, clienteId);
            if (producto == null)
            {
                throw new InvalidOperationException("El producto seleccionado no pertenece al cliente actual.");
            }

            if (subRazon == null)
            {
                throw new InvalidOperationException("Cada solicitud debe estar asignada a una subrazón social del cliente.");
            }

            var nowUtc = DateTime.UtcNow;
            var detalle = BuildSolicitudDetalle(subRazon, producto, solicitante, input.Cantidad);
            var solicitud = new Entity(SolicitudEntity)
            {
                [SolicitudNombre] = $"Aumento licencias - {producto.Nombre}",
                [SolicitudCliente] = new EntityReference(ClienteEntity, clienteId),
                [SolicitudCantidad] = input.Cantidad,
                [SolicitudEstado] = new OptionSetValue(SolicitudEstadoPendiente),
                [SolicitudFecha] = nowUtc,
                [SolicitudFechaProrrateo] = nowUtc.Date,
                [SolicitudDetalle] = detalle,
                [SolicitudValorUnitario] = producto.PrecioUnitarioUsd
            };

            if (producto.ProductoReference != null)
            {
                solicitud[SolicitudProducto] = producto.ProductoReference;
            }

            if (schema.SolicitudSubRazonLookupReady)
            {
                solicitud[SolicitudSubRazon] = new EntityReference(SubRazonEntity, subRazon.Id);
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                solicitud[SolicitudRegistroProductoCloud] = new EntityReference(ProductoCloudEntity, producto.SalesRecordId);
            }

            if (schema.SolicitadoPorReady)
            {
                solicitud[SolicitudSolicitadoPor] = solicitante;
            }

            await _svc.CreateAsync(solicitud);
            return clienteId;
        }

        public async Task<Guid> ActualizarFechaCorteAsync(IReadOnlyList<string> candidateEmails, ActualizarFechaCorteLicenciamientoVm input)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para editar la fecha de corte.");
            }

            if (input.DiaCorte is < 1 or > 31)
            {
                throw new InvalidOperationException("El día de facturación debe estar entre 1 y 31.");
            }

            var productos = await GetProductosCloudAsync(clienteId);
            foreach (var producto in productos)
            {
                var entity = new Entity(ProductoCloudEntity, producto.SalesRecordId)
                {
                    [ProductoCloudDiaFacturacion] = input.DiaCorte
                };

                await _svc.UpdateAsync(entity);
            }

            return clienteId;
        }

        private async Task<Guid> ResolveAuthorizedClientIdAsync(
            IReadOnlyList<string> candidateEmails,
            Guid? requestedClientId,
            List<ClienteLookupVm>? knownClients)
        {
            var isAdmin = IsAdminLicenciamiento(candidateEmails);
            if (isAdmin && requestedClientId.HasValue && requestedClientId.Value != Guid.Empty)
            {
                return requestedClientId.Value;
            }

            foreach (var email in candidateEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                var clienteId = await _clientesService.GetClienteIdByEmailAsync(email);
                if (clienteId != Guid.Empty)
                {
                    return clienteId;
                }
            }

            if (isAdmin)
            {
                knownClients ??= await GetClientesAsync();
                return knownClients.FirstOrDefault()?.Id ?? Guid.Empty;
            }

            return Guid.Empty;
        }

        private async Task<List<ClienteLookupVm>> GetClientesAsync()
        {
            var query = new QueryExpression(ClienteEntity)
            {
                ColumnSet = new ColumnSet("cr07a_clienteid", ClienteNombre)
            };
            query.AddOrder(ClienteNombre, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities
                .Select(e => new ClienteLookupVm
                {
                    Id = e.Id,
                    Nombre = e.GetAttributeValue<string>(ClienteNombre) ?? "(sin nombre)"
                })
                .Where(c => c.Id != Guid.Empty)
                .ToList();
        }

        private async Task<string> GetClienteNombreAsync(Guid clienteId)
        {
            try
            {
                var entity = await _svc.RetrieveAsync(ClienteEntity, clienteId, new ColumnSet(ClienteNombre));
                return entity.GetAttributeValue<string>(ClienteNombre) ?? "Cliente";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el nombre del cliente {ClienteId}", clienteId);
                return "Cliente";
            }
        }

        private async Task<List<ProductoCloudDto>> GetProductosCloudAsync(Guid clienteId)
        {
            var query = new QueryExpression(ProductoCloudEntity)
            {
                ColumnSet = new ColumnSet(
                    ProductoCloudId,
                    ProductoCloudCliente,
                    ProductoCloudProducto,
                    ProductoCloudNombre,
                    ProductoCloudCantidad,
                    ProductoCloudPrecioDecimal,
                    ProductoCloudPrecioMoney,
                    ProductoCloudDiaFacturacion,
                    "statecode"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(ProductoCloudCliente, ConditionOperator.Equal, clienteId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };
            query.AddOrder(ProductoCloudNombre, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities
                .Select(ToProductoCloudDto)
                .Where(p => p.SalesRecordId != Guid.Empty && p.Cantidad > 0)
                .ToList();
        }

        private async Task<ProductoCloudDto?> GetProductoCloudAsync(Guid salesRecordId, Guid clienteId)
        {
            if (salesRecordId == Guid.Empty)
            {
                return null;
            }

            try
            {
                var entity = await _svc.RetrieveAsync(
                    ProductoCloudEntity,
                    salesRecordId,
                    new ColumnSet(
                        ProductoCloudId,
                        ProductoCloudCliente,
                        ProductoCloudProducto,
                        ProductoCloudNombre,
                        ProductoCloudCantidad,
                        ProductoCloudPrecioDecimal,
                        ProductoCloudPrecioMoney,
                        ProductoCloudDiaFacturacion));

                var clienteRef = entity.GetAttributeValue<EntityReference>(ProductoCloudCliente);
                if (clienteRef?.Id != clienteId)
                {
                    return null;
                }

                return ToProductoCloudDto(entity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el producto cloud {SalesRecordId}", salesRecordId);
                return null;
            }
        }

        private static ProductoCloudDto ToProductoCloudDto(Entity entity)
        {
            var productoRef = entity.GetAttributeValue<EntityReference>(ProductoCloudProducto);
            var productName = entity.GetAttributeValue<string>(ProductoCloudNombre);
            var displayName = !string.IsNullOrWhiteSpace(productName)
                ? productName
                : productoRef?.Name ?? "Producto cloud";

            var price = entity.GetAttributeValue<decimal?>(ProductoCloudPrecioDecimal)
                ?? entity.GetAttributeValue<Money>(ProductoCloudPrecioMoney)?.Value
                ?? 0m;

            return new ProductoCloudDto(
                entity.Id,
                displayName,
                entity.GetAttributeValue<int?>(ProductoCloudCantidad) ?? 0,
                price,
                entity.GetAttributeValue<int?>(ProductoCloudDiaFacturacion),
                productoRef);
        }

        private async Task<List<SubRazonDto>> GetSubRazonesAsync(Guid clienteId)
        {
            try
            {
                var query = new QueryExpression(SubRazonEntity)
                {
                    ColumnSet = new ColumnSet(SubRazonNombre, SubRazonCliente),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression(SubRazonCliente, ConditionOperator.Equal, clienteId),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };
                query.AddOrder(SubRazonNombre, OrderType.Ascending);

                var result = await _svc.RetrieveMultipleAsync(query);
                return result.Entities
                    .Select(e => new SubRazonDto(e.Id, e.GetAttributeValue<string>(SubRazonNombre) ?? "(sin nombre)"))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron leer subrazones de licenciamiento.");
                return new List<SubRazonDto>();
            }
        }

        private async Task<SubRazonDto?> GetSubRazonAsync(Guid subRazonId, Guid clienteId)
        {
            if (subRazonId == Guid.Empty)
            {
                return null;
            }

            try
            {
                var entity = await _svc.RetrieveAsync(
                    SubRazonEntity,
                    subRazonId,
                    new ColumnSet(SubRazonNombre, SubRazonCliente));

                var clienteRef = entity.GetAttributeValue<EntityReference>(SubRazonCliente);
                if (clienteRef?.Id != clienteId)
                {
                    return null;
                }

                return new SubRazonDto(entity.Id, entity.GetAttributeValue<string>(SubRazonNombre) ?? "(sin nombre)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer la subrazón {SubRazonId}", subRazonId);
                return null;
            }
        }

        private async Task<Guid?> GetSubRazonByNameAsync(Guid clienteId, string nombre)
        {
            var query = new QueryExpression(SubRazonEntity)
            {
                ColumnSet = new ColumnSet(SubRazonNombre, SubRazonCliente),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(SubRazonCliente, ConditionOperator.Equal, clienteId),
                        new ConditionExpression(SubRazonNombre, ConditionOperator.Equal, nombre)
                    }
                }
            };

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities.FirstOrDefault()?.Id;
        }

        private async Task<List<AsignacionDto>> GetAsignacionesAsync(Guid clienteId)
        {
            try
            {
                var query = new QueryExpression(AsignacionEntity)
                {
                    ColumnSet = new ColumnSet(
                        AsignacionNombre,
                        AsignacionCliente,
                        AsignacionSubRazon,
                        AsignacionProductoCloud,
                        AsignacionCantidad),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression(AsignacionCliente, ConditionOperator.Equal, clienteId),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };

                var result = await _svc.RetrieveMultipleAsync(query);
                return result.Entities
                    .Select(e => new AsignacionDto(
                        e.Id,
                        e.GetAttributeValue<EntityReference>(AsignacionSubRazon)?.Id ?? Guid.Empty,
                        e.GetAttributeValue<EntityReference>(AsignacionProductoCloud)?.Id ?? Guid.Empty,
                        e.GetAttributeValue<int?>(AsignacionCantidad) ?? 0))
                    .Where(a => a.SubRazonId != Guid.Empty && a.SalesRecordId != Guid.Empty)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron leer asignaciones de licenciamiento.");
                return new List<AsignacionDto>();
            }
        }

        private async Task<AsignacionDto?> GetAsignacionAsync(Guid clienteId, Guid subRazonId, Guid salesRecordId)
        {
            var query = new QueryExpression(AsignacionEntity)
            {
                ColumnSet = new ColumnSet(AsignacionSubRazon, AsignacionProductoCloud, AsignacionCantidad),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(AsignacionCliente, ConditionOperator.Equal, clienteId),
                        new ConditionExpression(AsignacionSubRazon, ConditionOperator.Equal, subRazonId),
                        new ConditionExpression(AsignacionProductoCloud, ConditionOperator.Equal, salesRecordId)
                    }
                }
            };

            var result = await _svc.RetrieveMultipleAsync(query);
            var entity = result.Entities.FirstOrDefault();
            if (entity == null)
            {
                return null;
            }

            return new AsignacionDto(
                entity.Id,
                entity.GetAttributeValue<EntityReference>(AsignacionSubRazon)?.Id ?? Guid.Empty,
                entity.GetAttributeValue<EntityReference>(AsignacionProductoCloud)?.Id ?? Guid.Empty,
                entity.GetAttributeValue<int?>(AsignacionCantidad) ?? 0);
        }

        private async Task<int> GetTotalAsignadoProductoAsync(Guid clienteId, Guid salesRecordId, Guid? exceptAsignacionId)
        {
            var asignaciones = await GetAsignacionesAsync(clienteId);
            return asignaciones
                .Where(a => a.SalesRecordId == salesRecordId && (!exceptAsignacionId.HasValue || a.Id != exceptAsignacionId.Value))
                .Sum(a => a.Cantidad);
        }

        private async Task<List<SolicitudDto>> GetSolicitudesAsync(
            Guid clienteId,
            LicenciamientoSchemaStatus schema,
            IReadOnlyDictionary<Guid, ProductoCloudDto> productosPorId,
            IReadOnlyDictionary<Guid, SubRazonDto> subRazonesPorId)
        {
            var columns = new List<string>
            {
                SolicitudNombre,
                SolicitudCliente,
                SolicitudProducto,
                SolicitudCantidad,
                SolicitudEstado,
                SolicitudFecha,
                SolicitudFechaProrrateo,
                SolicitudDetalle,
                SolicitudValorUnitario,
                "createdon"
            };

            if (schema.SolicitudSubRazonLookupReady)
            {
                columns.Add(SolicitudSubRazon);
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                columns.Add(SolicitudRegistroProductoCloud);
            }

            if (schema.SolicitadoPorReady)
            {
                columns.Add(SolicitudSolicitadoPor);
            }

            var query = new QueryExpression(SolicitudEntity)
            {
                ColumnSet = new ColumnSet(columns.ToArray()),
                TopCount = 200,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(SolicitudCliente, ConditionOperator.Equal, clienteId)
                    }
                }
            };
            query.AddOrder("createdon", OrderType.Descending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities.Select(e =>
            {
                var detalle = e.GetAttributeValue<string>(SolicitudDetalle) ?? string.Empty;
                var registroRef = schema.SolicitudRegistroProductoCloudLookupReady
                    ? e.GetAttributeValue<EntityReference>(SolicitudRegistroProductoCloud)
                    : null;
                var salesRecordId = registroRef?.Id ?? TryReadGuid(detalle, "SalesRecordId");
                productosPorId.TryGetValue(salesRecordId, out var producto);

                var subRazonRef = schema.SolicitudSubRazonLookupReady
                    ? e.GetAttributeValue<EntityReference>(SolicitudSubRazon)
                    : null;
                var subRazonId = subRazonRef?.Id ?? TryReadGuid(detalle, "SubrazonId");
                var subRazonNombre = subRazonId != Guid.Empty && subRazonesPorId.TryGetValue(subRazonId, out var subRazon)
                    ? subRazon.Nombre
                    : TryReadValue(detalle, "Subrazon");

                var productoRef = e.GetAttributeValue<EntityReference>(SolicitudProducto);
                var productoNombre = producto?.Nombre
                    ?? TryReadValue(detalle, "Producto")
                    ?? productoRef?.Name
                    ?? e.GetAttributeValue<string>(SolicitudNombre)
                    ?? "Producto";

                var solicitadoPor = schema.SolicitadoPorReady
                    ? e.GetAttributeValue<string>(SolicitudSolicitadoPor)
                    : null;

                return new SolicitudDto(
                    e.Id,
                    salesRecordId == Guid.Empty ? null : salesRecordId,
                    subRazonId == Guid.Empty ? null : subRazonId,
                    subRazonNombre ?? "Sin subrazón",
                    productoNombre,
                    e.GetAttributeValue<int?>(SolicitudCantidad) ?? 0,
                    e.GetAttributeValue<decimal?>(SolicitudValorUnitario) ?? producto?.PrecioUnitarioUsd ?? 0m,
                    e.GetAttributeValue<DateTime?>(SolicitudFecha) ?? e.GetAttributeValue<DateTime?>("createdon"),
                    e.GetAttributeValue<DateTime?>(SolicitudFechaProrrateo),
                    e.GetAttributeValue<OptionSetValue>(SolicitudEstado)?.Value ?? SolicitudEstadoPendiente,
                    solicitadoPor ?? TryReadValue(detalle, "SolicitadoPor") ?? string.Empty);
            }).ToList();
        }

        private async Task<LicenciamientoSchemaStatus> EnsureSchemaAsync()
        {
            if (CachedSchemaStatus != null)
            {
                return CachedSchemaStatus;
            }

            await SchemaLock.WaitAsync();
            try
            {
                if (CachedSchemaStatus != null)
                {
                    return CachedSchemaStatus;
                }

                var status = new LicenciamientoSchemaStatus();
                var publishRequired = false;
                MetadataTouchedDuringEnsure = false;

                try
                {
                    status.SubRazonReady = await EnsureSubRazonEntityAsync();
                    status.AsignacionReady = await EnsureAsignacionEntityAsync(status.SubRazonReady);

                    if (status.SubRazonReady)
                    {
                        status.SolicitudSubRazonLookupReady = await EnsureLookupAttributeAsync(
                            SolicitudEntity,
                            SolicitudSubRazon,
                            "Subrazón licenciamiento",
                            SubRazonEntity,
                            "cr07a_solicitudaprovisionamiento_subrazonlicenciamiento");
                    }

                    status.SolicitudRegistroProductoCloudLookupReady = await EnsureLookupAttributeAsync(
                        SolicitudEntity,
                        SolicitudRegistroProductoCloud,
                        "Registro producto cloud",
                        ProductoCloudEntity,
                        "cr07a_solicitudaprovisionamiento_registroproductocloud");

                    status.SolicitadoPorReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudSolicitadoPor,
                        "Solicitado por",
                        200);

                    publishRequired = MetadataTouchedDuringEnsure;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo asegurar todo el esquema de licenciamiento en Dataverse.");
                    status.SetupError = "No se pudo crear o validar todo el esquema de licenciamiento en Dataverse. Revisa permisos de personalización del usuario/aplicación.";
                    status.SubRazonReady = await EntityExistsAsync(SubRazonEntity);
                    status.AsignacionReady = await EntityExistsAsync(AsignacionEntity);
                    status.SolicitudSubRazonLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudSubRazon);
                    status.SolicitudRegistroProductoCloudLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudRegistroProductoCloud);
                    status.SolicitadoPorReady = await AttributeExistsAsync(SolicitudEntity, SolicitudSolicitadoPor);
                }

                if (publishRequired)
                {
                    await PublishEntitiesAsync(SubRazonEntity, AsignacionEntity, SolicitudEntity);
                }

                CachedSchemaStatus = status;
                return status;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private async Task<bool> EnsureSubRazonEntityAsync()
        {
            if (!await EntityExistsAsync(SubRazonEntity))
            {
                var request = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "cr07a_SubrazonSocialLicenciamiento",
                        DisplayName = Label("Subrazón social licenciamiento"),
                        DisplayCollectionName = Label("Subrazones sociales licenciamiento"),
                        Description = Label("Subrazones sociales hijas asociadas a una razón social padre para licenciamiento."),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "cr07a_name",
                        DisplayName = Label("Nombre"),
                        RequiredLevel = RequiredLevel(AttributeRequiredLevel.ApplicationRequired),
                        MaxLength = 200
                    }
                };

                await _svc.ExecuteAsync(request);
                MarkMetadataUpdated();
            }

            await EnsureLookupAttributeAsync(
                SubRazonEntity,
                SubRazonCliente,
                "Cliente padre",
                ClienteEntity,
                "cr07a_cliente_subrazonsociallicenciamiento");

            return true;
        }

        private async Task<bool> EnsureAsignacionEntityAsync(bool subRazonReady)
        {
            if (!subRazonReady)
            {
                return false;
            }

            if (!await EntityExistsAsync(AsignacionEntity))
            {
                var request = new CreateEntityRequest
                {
                    Entity = new EntityMetadata
                    {
                        SchemaName = "cr07a_AsignacionLicenciamiento",
                        DisplayName = Label("Asignación licenciamiento"),
                        DisplayCollectionName = Label("Asignaciones licenciamiento"),
                        Description = Label("Asignación de licencias cloud por subrazón social."),
                        OwnershipType = OwnershipTypes.UserOwned,
                        IsActivity = false
                    },
                    PrimaryAttribute = new StringAttributeMetadata
                    {
                        SchemaName = "cr07a_name",
                        DisplayName = Label("Nombre"),
                        RequiredLevel = RequiredLevel(AttributeRequiredLevel.ApplicationRequired),
                        MaxLength = 250
                    }
                };

                await _svc.ExecuteAsync(request);
                MarkMetadataUpdated();
            }

            await EnsureLookupAttributeAsync(
                AsignacionEntity,
                AsignacionCliente,
                "Cliente padre",
                ClienteEntity,
                "cr07a_cliente_asignacionlicenciamiento");

            await EnsureLookupAttributeAsync(
                AsignacionEntity,
                AsignacionSubRazon,
                "Subrazón social",
                SubRazonEntity,
                "cr07a_subrazonsocial_asignacionlicenciamiento");

            await EnsureLookupAttributeAsync(
                AsignacionEntity,
                AsignacionProductoCloud,
                "Registro producto cloud",
                ProductoCloudEntity,
                "cr07a_salesperformancerecord_asignacionlicenciamiento");

            await EnsureIntegerAttributeAsync(
                AsignacionEntity,
                AsignacionCantidad,
                "Cantidad asignada",
                0,
                1000000);

            return true;
        }

        private async Task<bool> EnsureStringAttributeAsync(string entityName, string logicalName, string displayName, int maxLength)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new StringAttributeMetadata
                {
                    SchemaName = ToSchemaName(logicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None),
                    MaxLength = maxLength
                }
            });

            MarkMetadataUpdated();
            return true;
        }

        private async Task<bool> EnsureIntegerAttributeAsync(string entityName, string logicalName, string displayName, int min, int max)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new IntegerAttributeMetadata
                {
                    SchemaName = ToSchemaName(logicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None),
                    Format = IntegerFormat.None,
                    MinValue = min,
                    MaxValue = max
                }
            });

            MarkMetadataUpdated();
            return true;
        }

        private async Task<bool> EnsureLookupAttributeAsync(
            string referencingEntity,
            string lookupLogicalName,
            string displayName,
            string referencedEntity,
            string relationshipSchemaName)
        {
            if (await AttributeExistsAsync(referencingEntity, lookupLogicalName))
            {
                return true;
            }

            var request = new CreateOneToManyRequest
            {
                Lookup = new LookupAttributeMetadata
                {
                    SchemaName = ToSchemaName(lookupLogicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata
                {
                    SchemaName = relationshipSchemaName,
                    ReferencedEntity = referencedEntity,
                    ReferencingEntity = referencingEntity,
                    AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                    {
                        Behavior = AssociatedMenuBehavior.UseCollectionName,
                        Group = AssociatedMenuGroup.Details,
                        Label = Label(displayName),
                        Order = 10000
                    },
                    CascadeConfiguration = new CascadeConfiguration
                    {
                        Assign = CascadeType.NoCascade,
                        Delete = CascadeType.RemoveLink,
                        Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade,
                        Unshare = CascadeType.NoCascade
                    }
                }
            };

            await _svc.ExecuteAsync(request);
            MarkMetadataUpdated();
            return true;
        }

        private async Task<bool> EntityExistsAsync(string logicalName)
        {
            try
            {
                await _svc.ExecuteAsync(new RetrieveEntityRequest
                {
                    LogicalName = logicalName,
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
                return true;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo validar la entidad {EntityName}", logicalName);
                return false;
            }
        }

        private async Task<bool> AttributeExistsAsync(string entityName, string logicalName)
        {
            try
            {
                await _svc.ExecuteAsync(new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityName,
                    LogicalName = logicalName,
                    RetrieveAsIfPublished = true
                });
                return true;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo validar el atributo {EntityName}.{AttributeName}", entityName, logicalName);
                return false;
            }
        }

        private async Task PublishEntitiesAsync(params string[] entityNames)
        {
            var parameterXml = string.Join(string.Empty, entityNames.Select(e => $"<entity>{e}</entity>"));
            var request = new OrganizationRequest("PublishXml");
            request["ParameterXml"] = $"<importexportxml><entities>{parameterXml}</entities><nodes/><securityroles/><settings/><workflows/></importexportxml>";
            await _svc.ExecuteAsync(request);
        }

        private static void MarkMetadataUpdated()
        {
            MetadataTouchedDuringEnsure = true;
        }

        private static Label Label(string text) => new(text, LabelLanguage);

        private static AttributeRequiredLevelManagedProperty RequiredLevel(AttributeRequiredLevel level)
            => new(level);

        private static string ToSchemaName(string logicalName)
        {
            if (!logicalName.StartsWith("cr07a_", StringComparison.OrdinalIgnoreCase))
            {
                return logicalName;
            }

            var raw = logicalName["cr07a_".Length..];
            var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return logicalName;
            }

            return "cr07a_" + string.Concat(parts.Select(CultureInfo.InvariantCulture.TextInfo.ToTitleCase));
        }

        private static bool IsAdminLicenciamiento(IReadOnlyList<string> candidateEmails)
        {
            return candidateEmails.Any(email =>
                email.Equals(AdminLicenciamientoEmail, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSolicitudFacturable(int estado)
        {
            return estado is SolicitudEstadoAprovisionado or SolicitudEstadoAprobado;
        }

        private static string EstadoSolicitudLabel(int estado)
        {
            return estado switch
            {
                SolicitudEstadoAprovisionado => "Aprovisionado",
                SolicitudEstadoAprobado => "Aprobado para aprovisionar",
                _ => "Pendiente"
            };
        }

        private static string BuildSolicitudDetalle(SubRazonDto subRazon, ProductoCloudDto producto, string solicitante, int cantidad)
        {
            return string.Join(";",
                "PortalLicenciamiento",
                $"SubrazonId={subRazon.Id:D}",
                $"Subrazon={EscapeDetail(subRazon.Nombre)}",
                $"SalesRecordId={producto.SalesRecordId:D}",
                $"Producto={EscapeDetail(producto.Nombre)}",
                $"SolicitadoPor={EscapeDetail(solicitante)}",
                $"Cantidad={cantidad.ToString(CultureInfo.InvariantCulture)}");
        }

        private static string EscapeDetail(string value)
        {
            return (value ?? string.Empty).Replace(";", ",").Replace("=", ":", StringComparison.Ordinal);
        }

        private static string? TryReadValue(string detail, string key)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return null;
            }

            var prefix = key + "=";
            return detail.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?[prefix.Length..];
        }

        private static Guid TryReadGuid(string detail, string key)
        {
            var raw = TryReadValue(detail, key);
            return Guid.TryParse(raw, out var value) ? value : Guid.Empty;
        }

        private sealed record ProductoCloudDto(
            Guid SalesRecordId,
            string Nombre,
            int Cantidad,
            decimal PrecioUnitarioUsd,
            int? DiaFacturacion,
            EntityReference? ProductoReference);

        private sealed record SubRazonDto(Guid Id, string Nombre);

        private sealed record AsignacionDto(Guid Id, Guid SubRazonId, Guid SalesRecordId, int Cantidad);

        private sealed record SolicitudDto(
            Guid Id,
            Guid? SalesRecordId,
            Guid? SubRazonId,
            string SubRazonNombre,
            string ProductoNombre,
            int Cantidad,
            decimal PrecioUnitarioUsd,
            DateTime? FechaSolicitud,
            DateTime? FechaProrrateo,
            int EstadoValor,
            string SolicitadoPor);

        private sealed class LicenciamientoSchemaStatus
        {
            public bool SubRazonReady { get; set; }
            public bool AsignacionReady { get; set; }
            public bool SolicitudSubRazonLookupReady { get; set; }
            public bool SolicitudRegistroProductoCloudLookupReady { get; set; }
            public bool SolicitadoPorReady { get; set; }
            public string? SetupError { get; set; }
        }
    }
}
