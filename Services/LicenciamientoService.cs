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

        private const string AccountIdEntity = "cr07a_accountidicp";
        private const string AccountIdName = "cr07a_name";
        private const string AccountIdCliente = "cr07a_cliente";
        private const string AccountIdGrupoEmpresarialId = "cr07a_grupoempresarialid";
        private const string AccountIdGrupoEmpresarialName = "cr07a_grupoempresarialname";

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
        private const string SolicitudRegistroProductoCloud = "cr07a_registroproductocloud";
        private const string SolicitudSolicitadoPor = "cr07a_solicitadopor";
        private const string SolicitudGrupoEmpresarialId = "cr07a_grupoempresarialid";
        private const string SolicitudGrupoEmpresarialName = "cr07a_grupoempresarialname";
        private const string SolicitudClienteHijoName = "cr07a_clientehijoname";
        private const string SolicitudAccountIds = "cr07a_accountids";

        private const int SolicitudEstadoPendiente = 645250000;
        private const int SolicitudEstadoAprovisionado = 645250001;
        private const int SolicitudEstadoAprobado = 645250002;
        private const int LabelLanguage = 3082;

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
            var today = DateTime.Today;
            var selectedMonth = mes.GetValueOrDefault(today.Month);
            var selectedYear = anio.GetValueOrDefault(today.Year);

            if (selectedMonth is < 1 or > 12)
            {
                selectedMonth = today.Month;
            }

            if (selectedYear is < 2000 or > 2100)
            {
                selectedYear = today.Year;
            }

            var schema = await EnsureSchemaAsync();
            var canSwitchClient = IsAdminLicenciamiento(candidateEmails);
            var clientes = canSwitchClient ? await GetClientesAsync() : new List<ClienteLookupVm>();
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, requestedClientId, clientes);
            var diasMes = DateTime.DaysInMonth(selectedYear, selectedMonth);

            if (clienteId == Guid.Empty)
            {
                return new LicenciamientoViewModel
                {
                    MesSeleccionado = selectedMonth,
                    AnioSeleccionado = selectedYear,
                    DiasMes = diasMes,
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

            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var productos = await GetProductosCloudAsync(enterprise.ChildrenById.Keys.ToList(), enterprise.ChildrenById);
            var productosPorId = productos.ToDictionary(p => p.SalesRecordId);
            var solicitudes = await GetSolicitudesAsync(
                enterprise.ChildrenById.Keys.ToList(),
                schema,
                productosPorId,
                enterprise.ChildrenById,
                enterprise.GrupoEmpresarialNombre);

            var clientesHijos = enterprise.Children
                .OrderBy(c => c.ClienteNombre, StringComparer.OrdinalIgnoreCase)
                .Select(c => new ClienteLicenciamientoVm
                {
                    ClienteId = c.ClienteId,
                    ClienteNombre = c.ClienteNombre,
                    AccountIds = c.AccountIds,
                    Consumo = productos
                        .Where(p => p.ClienteId == c.ClienteId && p.Cantidad > 0)
                        .Select(p => new ConsumoLicenciaVm
                        {
                            SalesRecordId = p.SalesRecordId,
                            ClienteId = p.ClienteId,
                            ClienteNombre = p.ClienteNombre,
                            AccountId = string.Join(", ", p.AccountIds),
                            ProductoId = p.ProductoReference?.Id,
                            ProductoLogicalName = p.ProductoReference?.LogicalName ?? string.Empty,
                            Producto = p.Nombre,
                            Cantidad = p.Cantidad,
                            DiasConsumo = diasMes,
                            DiasMes = diasMes,
                            PrecioUnitarioUsd = p.PrecioUnitarioUsd,
                            Origen = "Mes completo"
                        })
                        .OrderBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(c => c.PrecioUnitarioUsd)
                        .ToList()
                })
                .ToList();

            foreach (var solicitud in solicitudes.Where(s => IsSolicitudFacturable(s.EstadoValor)))
            {
                if (!solicitud.SalesRecordId.HasValue ||
                    !productosPorId.TryGetValue(solicitud.SalesRecordId.Value, out var producto))
                {
                    continue;
                }

                var fechaBase = (solicitud.FechaProrrateo ?? solicitud.FechaSolicitud ?? new DateTime(selectedYear, selectedMonth, 1)).Date;
                if (fechaBase.Year != selectedYear || fechaBase.Month != selectedMonth)
                {
                    continue;
                }

                var clienteVm = clientesHijos.FirstOrDefault(c => c.ClienteId == solicitud.ClienteId);
                if (clienteVm == null)
                {
                    continue;
                }

                clienteVm.Consumo.Add(new ConsumoLicenciaVm
                {
                    SalesRecordId = producto.SalesRecordId,
                    SolicitudId = solicitud.Id,
                    ClienteId = solicitud.ClienteId,
                    ClienteNombre = solicitud.ClienteNombre,
                    AccountId = string.Join(", ", producto.AccountIds),
                    ProductoId = producto.ProductoReference?.Id,
                    ProductoLogicalName = producto.ProductoReference?.LogicalName ?? string.Empty,
                    Producto = producto.Nombre,
                    Cantidad = solicitud.Cantidad,
                    DiasConsumo = Math.Max(1, diasMes - fechaBase.Day + 1),
                    DiasMes = diasMes,
                    PrecioUnitarioUsd = solicitud.PrecioUnitarioUsd > 0 ? solicitud.PrecioUnitarioUsd : producto.PrecioUnitarioUsd,
                    EsProrrateoSolicitud = true,
                    Origen = $"Prorrateo desde {fechaBase:dd/MM/yyyy}"
                });
            }

            var productosRazonPadre = productos
                .GroupBy(p => new
                {
                    ProductoKey = GetProductGroupKey(p),
                    Precio = p.PrecioUnitarioUsd
                })
                .Select(g =>
                {
                    var first = g.First();
                    var clientesConProducto = g
                        .Select(p => p.ClienteNombre)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new LicenciaProductoResumenVm
                    {
                        SalesRecordId = first.SalesRecordId,
                        ProductoId = first.ProductoReference?.Id,
                        ProductoLogicalName = first.ProductoReference?.LogicalName ?? string.Empty,
                        Producto = first.Nombre,
                        CantidadTotal = g.Sum(p => p.Cantidad),
                        CantidadAsignada = g.Sum(p => p.Cantidad),
                        PrecioUnitarioUsd = first.PrecioUnitarioUsd,
                        DiaFacturacion = g.Select(p => p.DiaFacturacion).FirstOrDefault(d => d.HasValue),
                        ClientesConProducto = clientesConProducto,
                        AccountIds = g
                            .SelectMany(p => p.AccountIds)
                            .Where(a => !string.IsNullOrWhiteSpace(a))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    };
                })
                .OrderBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PrecioUnitarioUsd)
                .ToList();

            var diaCorte = productos.FirstOrDefault(p => p.DiaFacturacion.HasValue)?.DiaFacturacion ?? 15;
            var fechaCorte = new DateTime(selectedYear, selectedMonth, Math.Min(diaCorte, diasMes));

            return new LicenciamientoViewModel
            {
                ClienteId = clienteId,
                ClienteNombre = clienteNombre,
                GrupoEmpresarialId = enterprise.GrupoEmpresarialId,
                GrupoEmpresarialNombre = enterprise.GrupoEmpresarialNombre,
                TieneGrupoEmpresarial = enterprise.TieneGrupoEmpresarial,
                FechaCorte = fechaCorte,
                DiaCorte = diaCorte,
                MesSeleccionado = selectedMonth,
                AnioSeleccionado = selectedYear,
                DiasMes = diasMes,
                PuedeCambiarCliente = canSwitchClient,
                PuedeEditarEstructura = true,
                ClienteSeleccionadoId = clienteId,
                ClientesDisponibles = clientes,
                ProductosRazonPadre = productosRazonPadre,
                ClientesHijos = clientesHijos,
                HistoricoSolicitudes = solicitudes
                    .Select(s => new SolicitudLicenciaVm
                    {
                        Id = s.Id,
                        FechaSolicitud = s.FechaSolicitud,
                        FechaProrrateo = s.FechaProrrateo,
                        SolicitadoPor = s.SolicitadoPor,
                        ClienteHijo = s.ClienteNombre,
                        SubRazon = s.ClienteNombre,
                        GrupoEmpresarial = s.GrupoEmpresarialNombre,
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

        public Task<Guid> CrearSubRazonAsync(IReadOnlyList<string> candidateEmails, CrearSubRazonLicenciamientoVm input)
        {
            throw new InvalidOperationException("La estructura de licenciamiento ahora se administra desde la tabla Account ID icp con GrupoempresarialID y grupoempresarialname.");
        }

        public Task<Guid> GuardarAsignacionAsync(IReadOnlyList<string> candidateEmails, GuardarAsignacionLicenciamientoVm input)
        {
            throw new InvalidOperationException("Las asignaciones manuales fueron reemplazadas por clientes hijos reales asociados a Account ID icp.");
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
            if (!schema.SolicitudEntityReady || !schema.SolicitudClienteLookupReady)
            {
                throw new InvalidOperationException("La tabla de solicitudes de aprovisionamiento no está lista en Dataverse.");
            }

            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var clienteHijoId = input.ClienteHijoId != Guid.Empty ? input.ClienteHijoId : clienteId;
            if (!enterprise.ChildrenById.TryGetValue(clienteHijoId, out var clienteHijo))
            {
                throw new InvalidOperationException("El cliente hijo seleccionado no pertenece al grupo empresarial visible para este usuario.");
            }

            var producto = await GetProductoCloudAsync(input.SalesRecordId, clienteHijoId, enterprise.ChildrenById);
            if (producto == null)
            {
                throw new InvalidOperationException("El producto seleccionado no pertenece al cliente hijo seleccionado.");
            }

            var nowUtc = DateTime.UtcNow;
            var detalle = BuildSolicitudDetalle(enterprise, clienteHijo, producto, solicitante, input.Cantidad);
            var solicitud = new Entity(SolicitudEntity)
            {
                [SolicitudNombre] = $"Aumento licencias - {producto.Nombre}",
                [SolicitudCliente] = new EntityReference(ClienteEntity, clienteHijo.ClienteId),
                [SolicitudCantidad] = input.Cantidad,
                [SolicitudEstado] = new OptionSetValue(SolicitudEstadoPendiente),
                [SolicitudFecha] = nowUtc,
                [SolicitudFechaProrrateo] = nowUtc.Date,
                [SolicitudDetalle] = detalle,
                [SolicitudValorUnitario] = producto.PrecioUnitarioUsd
            };

            if (schema.SolicitudProductoLookupReady && producto.ProductoReference != null)
            {
                solicitud[SolicitudProducto] = producto.ProductoReference;
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                solicitud[SolicitudRegistroProductoCloud] = new EntityReference(ProductoCloudEntity, producto.SalesRecordId);
            }

            if (schema.SolicitadoPorReady)
            {
                solicitud[SolicitudSolicitadoPor] = solicitante;
            }

            if (schema.SolicitudGrupoEmpresarialIdReady)
            {
                solicitud[SolicitudGrupoEmpresarialId] = enterprise.GrupoEmpresarialId;
            }

            if (schema.SolicitudGrupoEmpresarialNameReady)
            {
                solicitud[SolicitudGrupoEmpresarialName] = enterprise.GrupoEmpresarialNombre;
            }

            if (schema.SolicitudClienteHijoNameReady)
            {
                solicitud[SolicitudClienteHijoName] = clienteHijo.ClienteNombre;
            }

            if (schema.SolicitudAccountIdsReady)
            {
                solicitud[SolicitudAccountIds] = string.Join(", ", clienteHijo.AccountIds);
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

            var schema = await EnsureSchemaAsync();
            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var productos = await GetProductosCloudAsync(enterprise.ChildrenById.Keys.ToList(), enterprise.ChildrenById);

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

        private async Task<EnterpriseContextDto> GetEnterpriseContextAsync(
            Guid selectedClienteId,
            string selectedClienteNombre,
            LicenciamientoSchemaStatus schema)
        {
            var ownRows = await GetAccountIdRowsByClienteAsync(selectedClienteId, schema);
            var anchorRow = ownRows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.GrupoEmpresarialId))
                ?? ownRows.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.GrupoEmpresarialName));

            var rows = anchorRow != null
                ? await GetAccountIdRowsByGroupAsync(anchorRow.GrupoEmpresarialId, anchorRow.GrupoEmpresarialName, schema)
                : ownRows;

            if (rows.Count == 0)
            {
                rows.Add(new AccountIdRowDto(
                    Guid.Empty,
                    selectedClienteId,
                    selectedClienteNombre,
                    string.Empty,
                    string.Empty,
                    string.Empty));
            }

            rows = await HydrateAccountRowsAsync(rows, selectedClienteId, selectedClienteNombre);

            var groupId = rows.Select(r => r.GrupoEmpresarialId).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
            var groupName = rows.Select(r => r.GrupoEmpresarialName).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? selectedClienteNombre;
            var children = rows
                .Where(r => r.ClienteId != Guid.Empty)
                .GroupBy(r => r.ClienteId)
                .Select(g => new ChildClienteDto(
                    g.Key,
                    g.Select(r => r.ClienteNombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Cliente",
                    g.Select(r => r.AccountId)
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .OrderBy(c => c.ClienteNombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (children.Count == 0)
            {
                children.Add(new ChildClienteDto(selectedClienteId, selectedClienteNombre, new List<string>()));
            }

            return new EnterpriseContextDto
            {
                GrupoEmpresarialId = groupId,
                GrupoEmpresarialNombre = groupName,
                TieneGrupoEmpresarial = !string.IsNullOrWhiteSpace(groupId)
                    || !string.IsNullOrWhiteSpace(anchorRow?.GrupoEmpresarialName)
                    || children.Count > 1,
                Children = children,
                ChildrenById = children.ToDictionary(c => c.ClienteId)
            };
        }

        private async Task<List<AccountIdRowDto>> GetAccountIdRowsByClienteAsync(Guid clienteId, LicenciamientoSchemaStatus schema)
        {
            var columns = GetAccountIdColumns(schema);
            var query = new QueryExpression(AccountIdEntity)
            {
                ColumnSet = new ColumnSet(columns.ToArray()),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(AccountIdCliente, ConditionOperator.Equal, clienteId),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };
            query.AddOrder(AccountIdName, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities.Select(e => ToAccountIdRowDto(e, schema)).Where(r => r.ClienteId != Guid.Empty).ToList();
        }

        private async Task<List<AccountIdRowDto>> GetAccountIdRowsByGroupAsync(
            string? grupoEmpresarialId,
            string? grupoEmpresarialName,
            LicenciamientoSchemaStatus schema)
        {
            if (!schema.AccountIdGrupoEmpresarialIdReady && !schema.AccountIdGrupoEmpresarialNameReady)
            {
                return new List<AccountIdRowDto>();
            }

            var groupFilter = new FilterExpression(LogicalOperator.Or);
            if (schema.AccountIdGrupoEmpresarialIdReady && !string.IsNullOrWhiteSpace(grupoEmpresarialId))
            {
                groupFilter.AddCondition(AccountIdGrupoEmpresarialId, ConditionOperator.Equal, grupoEmpresarialId);
            }

            if (schema.AccountIdGrupoEmpresarialNameReady && !string.IsNullOrWhiteSpace(grupoEmpresarialName))
            {
                groupFilter.AddCondition(AccountIdGrupoEmpresarialName, ConditionOperator.Equal, grupoEmpresarialName);
            }

            if (groupFilter.Conditions.Count == 0)
            {
                return new List<AccountIdRowDto>();
            }

            var query = new QueryExpression(AccountIdEntity)
            {
                ColumnSet = new ColumnSet(GetAccountIdColumns(schema).ToArray()),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                }
            };
            query.Criteria.AddFilter(groupFilter);
            query.AddOrder(AccountIdName, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities.Select(e => ToAccountIdRowDto(e, schema)).Where(r => r.ClienteId != Guid.Empty).ToList();
        }

        private async Task<List<AccountIdRowDto>> HydrateAccountRowsAsync(
            List<AccountIdRowDto> rows,
            Guid selectedClienteId,
            string selectedClienteNombre)
        {
            var names = new Dictionary<Guid, string>();
            if (selectedClienteId != Guid.Empty)
            {
                names[selectedClienteId] = selectedClienteNombre;
            }

            foreach (var clienteId in rows.Select(r => r.ClienteId).Where(id => id != Guid.Empty).Distinct())
            {
                if (!names.ContainsKey(clienteId))
                {
                    names[clienteId] = await GetClienteNombreAsync(clienteId);
                }
            }

            return rows
                .Select(r => r with
                {
                    ClienteNombre = !string.IsNullOrWhiteSpace(r.ClienteNombre) ? r.ClienteNombre : names.GetValueOrDefault(r.ClienteId, "Cliente")
                })
                .ToList();
        }

        private static List<string> GetAccountIdColumns(LicenciamientoSchemaStatus schema)
        {
            var columns = new List<string>
            {
                AccountIdName,
                AccountIdCliente,
                "statecode"
            };

            if (schema.AccountIdGrupoEmpresarialIdReady)
            {
                columns.Add(AccountIdGrupoEmpresarialId);
            }

            if (schema.AccountIdGrupoEmpresarialNameReady)
            {
                columns.Add(AccountIdGrupoEmpresarialName);
            }

            return columns;
        }

        private static AccountIdRowDto ToAccountIdRowDto(Entity entity, LicenciamientoSchemaStatus schema)
        {
            var clienteRef = entity.GetAttributeValue<EntityReference>(AccountIdCliente);
            return new AccountIdRowDto(
                entity.Id,
                clienteRef?.Id ?? Guid.Empty,
                clienteRef?.Name ?? string.Empty,
                entity.GetAttributeValue<string>(AccountIdName) ?? string.Empty,
                schema.AccountIdGrupoEmpresarialIdReady ? entity.GetAttributeValue<string>(AccountIdGrupoEmpresarialId) ?? string.Empty : string.Empty,
                schema.AccountIdGrupoEmpresarialNameReady ? entity.GetAttributeValue<string>(AccountIdGrupoEmpresarialName) ?? string.Empty : string.Empty);
        }

        private async Task<List<ProductoCloudDto>> GetProductosCloudAsync(
            IReadOnlyCollection<Guid> clienteIds,
            IReadOnlyDictionary<Guid, ChildClienteDto> childrenById)
        {
            if (clienteIds.Count == 0)
            {
                return new List<ProductoCloudDto>();
            }

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
                    "statecode")
            };

            if (clienteIds.Count == 1)
            {
                query.Criteria.AddCondition(ProductoCloudCliente, ConditionOperator.Equal, clienteIds.First());
            }
            else
            {
                query.Criteria.AddCondition(ProductoCloudCliente, ConditionOperator.In, clienteIds.Select(id => (object)id).ToArray());
            }

            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder(ProductoCloudNombre, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities
                .Select(e => ToProductoCloudDto(e, childrenById))
                .Where(p => p.SalesRecordId != Guid.Empty && p.ClienteId != Guid.Empty && p.Cantidad > 0)
                .ToList();
        }

        private async Task<ProductoCloudDto?> GetProductoCloudAsync(
            Guid salesRecordId,
            Guid clienteHijoId,
            IReadOnlyDictionary<Guid, ChildClienteDto> childrenById)
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
                if (clienteRef?.Id != clienteHijoId)
                {
                    return null;
                }

                return ToProductoCloudDto(entity, childrenById);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el producto cloud {SalesRecordId}", salesRecordId);
                return null;
            }
        }

        private static ProductoCloudDto ToProductoCloudDto(
            Entity entity,
            IReadOnlyDictionary<Guid, ChildClienteDto> childrenById)
        {
            var productoRef = entity.GetAttributeValue<EntityReference>(ProductoCloudProducto);
            var clienteRef = entity.GetAttributeValue<EntityReference>(ProductoCloudCliente);
            var clienteId = clienteRef?.Id ?? Guid.Empty;
            childrenById.TryGetValue(clienteId, out var child);
            var productName = entity.GetAttributeValue<string>(ProductoCloudNombre);
            var displayName = !string.IsNullOrWhiteSpace(productName)
                ? productName
                : productoRef?.Name ?? "Producto cloud";

            var price = entity.GetAttributeValue<decimal?>(ProductoCloudPrecioDecimal)
                ?? entity.GetAttributeValue<Money>(ProductoCloudPrecioMoney)?.Value
                ?? 0m;

            return new ProductoCloudDto(
                entity.Id,
                clienteId,
                child?.ClienteNombre ?? clienteRef?.Name ?? "Cliente",
                child?.AccountIds ?? new List<string>(),
                displayName,
                entity.GetAttributeValue<int?>(ProductoCloudCantidad) ?? 0,
                price,
                entity.GetAttributeValue<int?>(ProductoCloudDiaFacturacion),
                productoRef);
        }

        private async Task<List<SolicitudDto>> GetSolicitudesAsync(
            IReadOnlyCollection<Guid> clienteIds,
            LicenciamientoSchemaStatus schema,
            IReadOnlyDictionary<Guid, ProductoCloudDto> productosPorId,
            IReadOnlyDictionary<Guid, ChildClienteDto> childrenById,
            string grupoEmpresarialNombre)
        {
            if (!schema.SolicitudEntityReady || !schema.SolicitudClienteLookupReady || clienteIds.Count == 0)
            {
                return new List<SolicitudDto>();
            }

            var columns = new List<string>
            {
                SolicitudNombre,
                SolicitudCliente,
                SolicitudCantidad,
                SolicitudEstado,
                SolicitudFecha,
                SolicitudFechaProrrateo,
                SolicitudDetalle,
                SolicitudValorUnitario,
                "createdon"
            };

            if (schema.SolicitudProductoLookupReady)
            {
                columns.Add(SolicitudProducto);
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                columns.Add(SolicitudRegistroProductoCloud);
            }

            if (schema.SolicitadoPorReady)
            {
                columns.Add(SolicitudSolicitadoPor);
            }

            if (schema.SolicitudGrupoEmpresarialNameReady)
            {
                columns.Add(SolicitudGrupoEmpresarialName);
            }

            if (schema.SolicitudClienteHijoNameReady)
            {
                columns.Add(SolicitudClienteHijoName);
            }

            var query = new QueryExpression(SolicitudEntity)
            {
                ColumnSet = new ColumnSet(columns.ToArray()),
                TopCount = 300
            };

            if (clienteIds.Count == 1)
            {
                query.Criteria.AddCondition(SolicitudCliente, ConditionOperator.Equal, clienteIds.First());
            }
            else
            {
                query.Criteria.AddCondition(SolicitudCliente, ConditionOperator.In, clienteIds.Select(id => (object)id).ToArray());
            }

            query.AddOrder("createdon", OrderType.Descending);

            var result = await _svc.RetrieveMultipleAsync(query);
            return result.Entities.Select(e =>
            {
                var detalle = e.GetAttributeValue<string>(SolicitudDetalle) ?? string.Empty;
                var clienteRef = e.GetAttributeValue<EntityReference>(SolicitudCliente);
                var clienteId = clienteRef?.Id ?? Guid.Empty;
                childrenById.TryGetValue(clienteId, out var child);

                var registroRef = schema.SolicitudRegistroProductoCloudLookupReady
                    ? e.GetAttributeValue<EntityReference>(SolicitudRegistroProductoCloud)
                    : null;
                var salesRecordId = registroRef?.Id ?? TryReadGuid(detalle, "SalesRecordId");
                productosPorId.TryGetValue(salesRecordId, out var producto);

                var productoRef = schema.SolicitudProductoLookupReady
                    ? e.GetAttributeValue<EntityReference>(SolicitudProducto)
                    : null;
                var productoNombre = producto?.Nombre
                    ?? TryReadValue(detalle, "Producto")
                    ?? productoRef?.Name
                    ?? e.GetAttributeValue<string>(SolicitudNombre)
                    ?? "Producto";

                var solicitadoPor = schema.SolicitadoPorReady
                    ? e.GetAttributeValue<string>(SolicitudSolicitadoPor)
                    : null;

                var clienteNombre = schema.SolicitudClienteHijoNameReady
                    ? e.GetAttributeValue<string>(SolicitudClienteHijoName)
                    : null;

                var grupoNombre = schema.SolicitudGrupoEmpresarialNameReady
                    ? e.GetAttributeValue<string>(SolicitudGrupoEmpresarialName)
                    : null;

                return new SolicitudDto(
                    e.Id,
                    salesRecordId == Guid.Empty ? null : salesRecordId,
                    clienteId,
                    clienteNombre ?? child?.ClienteNombre ?? clienteRef?.Name ?? "Cliente",
                    grupoNombre ?? grupoEmpresarialNombre,
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
                MetadataTouchedDuringEnsure = false;

                try
                {
                    status.AccountIdGrupoEmpresarialIdReady = await EnsureStringAttributeAsync(
                        AccountIdEntity,
                        AccountIdGrupoEmpresarialId,
                        "GrupoempresarialID",
                        100);

                    status.AccountIdGrupoEmpresarialNameReady = await EnsureStringAttributeAsync(
                        AccountIdEntity,
                        AccountIdGrupoEmpresarialName,
                        "grupoempresarialname",
                        200);

                    status.SolicitudEntityReady = await EnsureSolicitudEntityAsync();
                    status.SolicitudClienteLookupReady = await EnsureLookupAttributeAsync(
                        SolicitudEntity,
                        SolicitudCliente,
                        "Cliente hijo",
                        ClienteEntity,
                        "cr07a_cliente_solicitudaprovisionamiento");

                    status.SolicitudCantidadReady = await EnsureIntegerAttributeAsync(
                        SolicitudEntity,
                        SolicitudCantidad,
                        "Cantidad",
                        0,
                        1000000);

                    status.SolicitudEstadoReady = await EnsureOptionSetAttributeAsync(
                        SolicitudEntity,
                        SolicitudEstado,
                        "Estado");

                    status.SolicitudFechaReady = await EnsureDateTimeAttributeAsync(
                        SolicitudEntity,
                        SolicitudFecha,
                        "Fecha de aprovisionamiento");

                    status.SolicitudFechaProrrateoReady = await EnsureDateTimeAttributeAsync(
                        SolicitudEntity,
                        SolicitudFechaProrrateo,
                        "Fecha prorrateo si aplica");

                    status.SolicitudDetalleReady = await EnsureMemoAttributeAsync(
                        SolicitudEntity,
                        SolicitudDetalle,
                        "Producto y cantidades",
                        4000);

                    status.SolicitudValorUnitarioReady = await EnsureDecimalAttributeAsync(
                        SolicitudEntity,
                        SolicitudValorUnitario,
                        "Valor unitario",
                        0m,
                        100000000m,
                        2);

                    status.SolicitudProductoLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudProducto);
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

                    status.SolicitudGrupoEmpresarialIdReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudGrupoEmpresarialId,
                        "Grupo empresarial ID",
                        100);

                    status.SolicitudGrupoEmpresarialNameReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudGrupoEmpresarialName,
                        "Grupo empresarial name",
                        200);

                    status.SolicitudClienteHijoNameReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudClienteHijoName,
                        "Cliente hijo name",
                        200);

                    status.SolicitudAccountIdsReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudAccountIds,
                        "Account IDs",
                        500);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo asegurar todo el esquema de licenciamiento en Dataverse.");
                    status.SetupError = "No se pudo crear o validar todo el esquema de licenciamiento en Dataverse. Revisa permisos de personalización del usuario/aplicación.";
                    status.AccountIdGrupoEmpresarialIdReady = await AttributeExistsAsync(AccountIdEntity, AccountIdGrupoEmpresarialId);
                    status.AccountIdGrupoEmpresarialNameReady = await AttributeExistsAsync(AccountIdEntity, AccountIdGrupoEmpresarialName);
                    status.SolicitudEntityReady = await EntityExistsAsync(SolicitudEntity);
                    status.SolicitudClienteLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudCliente);
                    status.SolicitudProductoLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudProducto);
                    status.SolicitudRegistroProductoCloudLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudRegistroProductoCloud);
                    status.SolicitadoPorReady = await AttributeExistsAsync(SolicitudEntity, SolicitudSolicitadoPor);
                    status.SolicitudGrupoEmpresarialIdReady = await AttributeExistsAsync(SolicitudEntity, SolicitudGrupoEmpresarialId);
                    status.SolicitudGrupoEmpresarialNameReady = await AttributeExistsAsync(SolicitudEntity, SolicitudGrupoEmpresarialName);
                    status.SolicitudClienteHijoNameReady = await AttributeExistsAsync(SolicitudEntity, SolicitudClienteHijoName);
                    status.SolicitudAccountIdsReady = await AttributeExistsAsync(SolicitudEntity, SolicitudAccountIds);
                }

                if (MetadataTouchedDuringEnsure)
                {
                    await PublishEntitiesAsync(AccountIdEntity, SolicitudEntity);
                }

                CachedSchemaStatus = status;
                return status;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private async Task<bool> EnsureSolicitudEntityAsync()
        {
            if (await EntityExistsAsync(SolicitudEntity))
            {
                return true;
            }

            var request = new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = "cr07a_SolicitudAprovisionamiento",
                    DisplayName = Label("Solicitud de aprovisionamiento"),
                    DisplayCollectionName = Label("Solicitudes de aprovisionamiento"),
                    Description = Label("Solicitudes de aumento de licencias asociadas al cliente hijo para prorrateo mensual."),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = SolicitudNombre,
                    DisplayName = Label("Nombre"),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.ApplicationRequired),
                    MaxLength = 250
                }
            };

            await _svc.ExecuteAsync(request);
            MarkMetadataUpdated();
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

        private async Task<bool> EnsureMemoAttributeAsync(string entityName, string logicalName, string displayName, int maxLength)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new MemoAttributeMetadata
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

        private async Task<bool> EnsureDecimalAttributeAsync(
            string entityName,
            string logicalName,
            string displayName,
            decimal min,
            decimal max,
            int precision)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new DecimalAttributeMetadata
                {
                    SchemaName = ToSchemaName(logicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None),
                    MinValue = min,
                    MaxValue = max,
                    Precision = precision
                }
            });

            MarkMetadataUpdated();
            return true;
        }

        private async Task<bool> EnsureDateTimeAttributeAsync(string entityName, string logicalName, string displayName)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = ToSchemaName(logicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None),
                    DateTimeBehavior = DateTimeBehavior.UserLocal,
                    Format = DateTimeFormat.DateAndTime
                }
            });

            MarkMetadataUpdated();
            return true;
        }

        private async Task<bool> EnsureOptionSetAttributeAsync(string entityName, string logicalName, string displayName)
        {
            if (await AttributeExistsAsync(entityName, logicalName))
            {
                return true;
            }

            await _svc.ExecuteAsync(new CreateAttributeRequest
            {
                EntityName = entityName,
                Attribute = new PicklistAttributeMetadata
                {
                    SchemaName = ToSchemaName(logicalName),
                    DisplayName = Label(displayName),
                    RequiredLevel = RequiredLevel(AttributeRequiredLevel.None),
                    OptionSet = new OptionSetMetadata
                    {
                        IsGlobal = false,
                        OptionSetType = OptionSetType.Picklist,
                        Options =
                        {
                            new OptionMetadata(Label("Pendiente"), SolicitudEstadoPendiente),
                            new OptionMetadata(Label("Aprovisionado"), SolicitudEstadoAprovisionado),
                            new OptionMetadata(Label("Aprobado para aprovisionar"), SolicitudEstadoAprobado)
                        }
                    }
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
            var parameterXml = string.Join(string.Empty, entityNames.Distinct(StringComparer.OrdinalIgnoreCase).Select(e => $"<entity>{e}</entity>"));
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

        private static string GetProductGroupKey(ProductoCloudDto producto)
        {
            if (producto.ProductoReference != null)
            {
                return $"{producto.ProductoReference.LogicalName}:{producto.ProductoReference.Id:D}";
            }

            return (producto.Nombre ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string BuildSolicitudDetalle(
            EnterpriseContextDto enterprise,
            ChildClienteDto clienteHijo,
            ProductoCloudDto producto,
            string solicitante,
            int cantidad)
        {
            return string.Join(";",
                "PortalLicenciamiento",
                $"GrupoEmpresarialId={EscapeDetail(enterprise.GrupoEmpresarialId)}",
                $"GrupoEmpresarial={EscapeDetail(enterprise.GrupoEmpresarialNombre)}",
                $"ClienteHijoId={clienteHijo.ClienteId:D}",
                $"ClienteHijo={EscapeDetail(clienteHijo.ClienteNombre)}",
                $"AccountIds={EscapeDetail(string.Join(",", clienteHijo.AccountIds))}",
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

        private sealed record AccountIdRowDto(
            Guid Id,
            Guid ClienteId,
            string ClienteNombre,
            string AccountId,
            string GrupoEmpresarialId,
            string GrupoEmpresarialName);

        private sealed record ChildClienteDto(Guid ClienteId, string ClienteNombre, List<string> AccountIds);

        private sealed record ProductoCloudDto(
            Guid SalesRecordId,
            Guid ClienteId,
            string ClienteNombre,
            List<string> AccountIds,
            string Nombre,
            int Cantidad,
            decimal PrecioUnitarioUsd,
            int? DiaFacturacion,
            EntityReference? ProductoReference);

        private sealed record SolicitudDto(
            Guid Id,
            Guid? SalesRecordId,
            Guid ClienteId,
            string ClienteNombre,
            string GrupoEmpresarialNombre,
            string ProductoNombre,
            int Cantidad,
            decimal PrecioUnitarioUsd,
            DateTime? FechaSolicitud,
            DateTime? FechaProrrateo,
            int EstadoValor,
            string SolicitadoPor);

        private sealed class EnterpriseContextDto
        {
            public string GrupoEmpresarialId { get; set; } = string.Empty;
            public string GrupoEmpresarialNombre { get; set; } = string.Empty;
            public bool TieneGrupoEmpresarial { get; set; }
            public List<ChildClienteDto> Children { get; set; } = new();
            public Dictionary<Guid, ChildClienteDto> ChildrenById { get; set; } = new();
        }

        private sealed class LicenciamientoSchemaStatus
        {
            public bool AccountIdGrupoEmpresarialIdReady { get; set; }
            public bool AccountIdGrupoEmpresarialNameReady { get; set; }
            public bool SolicitudEntityReady { get; set; }
            public bool SolicitudClienteLookupReady { get; set; }
            public bool SolicitudProductoLookupReady { get; set; }
            public bool SolicitudCantidadReady { get; set; }
            public bool SolicitudEstadoReady { get; set; }
            public bool SolicitudFechaReady { get; set; }
            public bool SolicitudFechaProrrateoReady { get; set; }
            public bool SolicitudDetalleReady { get; set; }
            public bool SolicitudValorUnitarioReady { get; set; }
            public bool SolicitudRegistroProductoCloudLookupReady { get; set; }
            public bool SolicitadoPorReady { get; set; }
            public bool SolicitudGrupoEmpresarialIdReady { get; set; }
            public bool SolicitudGrupoEmpresarialNameReady { get; set; }
            public bool SolicitudClienteHijoNameReady { get; set; }
            public bool SolicitudAccountIdsReady { get; set; }
            public string? SetupError { get; set; }
        }
    }
}
