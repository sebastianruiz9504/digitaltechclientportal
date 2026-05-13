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

        private const string SolicitudEntity = "cr07a_solicitudesclientes";
        private const string SolicitudEntitySchemaName = "cr07a_SolicitudesClientes";
        private const string SolicitudCliente = "cr07a_cliente";
        private const string SolicitudProducto = "cr07a_producto";
        private const string SolicitudCantidad = "cr07a_cantidad";
        private const string SolicitudEstado = "cr07a_estado";
        private const string SolicitudFecha = "cr07a_fechaaprovisionamiento";
        private const string SolicitudNombre = "cr07a_name";
        private const string SolicitudDetalle = "cr07a_detalle";
        private const string SolicitudValorUnitario = "cr07a_valorunitario";
        private const string SolicitudRegistroProductoCloud = "cr07a_registroproductocloud";
        private const string SolicitudSolicitadoPor = "cr07a_solicitadopor";
        private const string SolicitudSolicitadoPorCorreo = "cr07a_solicitadoporcorreo";
        private const string SolicitudGrupoEmpresarialId = "cr07a_grupoempresarialid";
        private const string SolicitudGrupoEmpresarialName = "cr07a_grupoempresarialname";
        private const string SolicitudClienteHijoName = "cr07a_clientehijoname";
        private const string SolicitudAccountIds = "cr07a_accountids";
        private const string SolicitudAprobadoPor = "cr07a_aprobadopor";
        private const string SolicitudFechaAprobacion = "cr07a_fechaaprobacion";
        private const string SolicitudTipoMovimiento = "cr07a_tipomovimiento";
        private const string ProductoCloudPrimaryName = "cr07a_name";

        private const int SolicitudEstadoPendiente = 645250000;
        private const int SolicitudEstadoAprovisionado = 645250001;
        private const int SolicitudEstadoRechazado = 645250002;
        private const int LabelLanguage = 3082;

        private const string MovimientoNuevaLicencia = "NuevaLicencia";
        private const string MovimientoDesasignacion = "Desasignacion";
        private const string MovimientoAsignacionDisponible = "AsignacionDisponible";

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
            var licenciasSinAsignar = BuildLicenciasSinAsignar(solicitudes);

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

            foreach (var solicitud in solicitudes.Where(s => IsSolicitudFacturable(s.EstadoValor) && IsNuevaLicencia(s.TipoMovimiento)))
            {
                if (!solicitud.SalesRecordId.HasValue ||
                    !productosPorId.TryGetValue(solicitud.SalesRecordId.Value, out var producto))
                {
                    continue;
                }

                var fechaBase = (solicitud.FechaAprovisionamiento ?? solicitud.FechaSolicitud ?? new DateTime(selectedYear, selectedMonth, 1)).Date;
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
                        ProductoKey = GetProductGroupKey(first),
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

            foreach (var libre in licenciasSinAsignar)
            {
                var resumen = productosRazonPadre.FirstOrDefault(p =>
                    string.Equals(p.ProductoKey, libre.ProductoKey, StringComparison.OrdinalIgnoreCase) &&
                    p.PrecioUnitarioUsd == libre.PrecioUnitarioUsd);

                if (resumen == null)
                {
                    resumen = new LicenciaProductoResumenVm
                    {
                        ProductoId = libre.ProductoId,
                        ProductoLogicalName = libre.ProductoLogicalName,
                        ProductoKey = libre.ProductoKey,
                        Producto = libre.Producto,
                        PrecioUnitarioUsd = libre.PrecioUnitarioUsd,
                        DiaFacturacion = libre.DiaFacturacion
                    };
                    productosRazonPadre.Add(resumen);
                }

                resumen.CantidadTotal += libre.Cantidad;
            }

            productosRazonPadre = productosRazonPadre
                .OrderBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PrecioUnitarioUsd)
                .ToList();

            var productosSolicitud = productos
                .OrderBy(p => p.ClienteNombre, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Nombre, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.PrecioUnitarioUsd)
                .Select(p => new ProductoSolicitudVm
                {
                    SalesRecordId = p.SalesRecordId,
                    ClienteId = p.ClienteId,
                    ClienteNombre = p.ClienteNombre,
                    Producto = p.Nombre,
                    Cantidad = p.Cantidad,
                    PrecioUnitarioUsd = p.PrecioUnitarioUsd
                })
                .ToList();

            var diaCorte = productos.FirstOrDefault(p => p.DiaFacturacion.HasValue)?.DiaFacturacion ?? 15;
            var fechaCorte = new DateTime(selectedYear, selectedMonth, Math.Min(diaCorte, diasMes));
            var accountIdsGrupoActual = enterprise.AccountRows
                .Where(r => r.Id != Guid.Empty)
                .Select(ToAccountIdLicenciamientoVm)
                .OrderBy(a => a.ClienteNombre, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var accountIdsDisponibles = canSwitchClient
                ? (await GetAllAccountIdRowsAsync(schema))
                    .Select(ToAccountIdLicenciamientoVm)
                    .OrderBy(a => a.ClienteNombre, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<AccountIdLicenciamientoVm>();

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
                PuedeEditarEstructura = canSwitchClient,
                PuedeMoverCantidades = enterprise.Children.Count > 1,
                ClienteSeleccionadoId = clienteId,
                ClientesDisponibles = clientes,
                AccountIdsDisponibles = accountIdsDisponibles,
                AccountIdsGrupoActual = accountIdsGrupoActual,
                ProductosRazonPadre = productosRazonPadre,
                ProductosSolicitud = productosSolicitud,
                LicenciasSinAsignar = licenciasSinAsignar,
                ClientesHijos = clientesHijos,
                HistoricoSolicitudes = solicitudes
                    .Select(s => new SolicitudLicenciaVm
                    {
                        Id = s.Id,
                        FechaSolicitud = s.FechaSolicitud,
                        FechaAprovisionamiento = s.FechaAprovisionamiento,
                        FechaProrrateo = s.FechaAprovisionamiento,
                        SolicitadoPor = s.SolicitadoPor,
                        ClienteHijo = s.ClienteNombre,
                        SubRazon = s.ClienteNombre,
                        GrupoEmpresarial = s.GrupoEmpresarialNombre,
                        Producto = s.ProductoNombre,
                        CantidadNueva = s.Cantidad,
                        PrecioUnitarioUsd = s.PrecioUnitarioUsd,
                        TipoMovimiento = TipoMovimientoLabel(s.TipoMovimiento),
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
            return await CrearSolicitudClienteAsync(
                candidateEmails,
                new CrearSolicitudClienteVm
                {
                    ClienteId = input.ClienteId,
                    Modo = MovimientoNuevaLicencia,
                    ClienteHijoId = input.ClienteHijoId,
                    SalesRecordId = input.SalesRecordId,
                    Cantidad = input.Cantidad,
                    FechaAprovisionamiento = DateTime.Today,
                    Mes = input.Mes,
                    Anio = input.Anio
                },
                solicitante);
        }

        public async Task<Guid> CrearSolicitudClienteAsync(
            IReadOnlyList<string> candidateEmails,
            CrearSolicitudClienteVm input,
            string solicitante)
        {
            var tipoMovimiento = NormalizeTipoMovimiento(input.Modo);
            if (IsDesasignacion(tipoMovimiento))
            {
                return await DesasignarLicenciasAsync(candidateEmails, input, solicitante);
            }

            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para solicitar licencias.");
            }

            if (input.Cantidad <= 0)
            {
                throw new InvalidOperationException("Solo se permiten solicitudes para subir licencias.");
            }

            if (input.FechaAprovisionamiento == default)
            {
                throw new InvalidOperationException("Selecciona la fecha de aprovisionamiento.");
            }

            var schema = await EnsureSchemaAsync();
            if (!schema.SolicitudEntityReady || !schema.SolicitudClienteLookupReady)
            {
                throw new InvalidOperationException("La tabla solicitudesclientes no está lista en Dataverse.");
            }

            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var clienteHijoId = input.ClienteHijoId != Guid.Empty ? input.ClienteHijoId : clienteId;
            if (!enterprise.ChildrenById.TryGetValue(clienteHijoId, out var clienteHijo))
            {
                throw new InvalidOperationException("El cliente hijo seleccionado no pertenece al grupo empresarial visible para este usuario.");
            }

            ProductoCloudDto? producto = null;
            var salesRecordId = input.SalesRecordId.GetValueOrDefault();
            if (salesRecordId != Guid.Empty)
            {
                producto = await GetProductoCloudAsync(salesRecordId, clienteHijoId, enterprise.ChildrenById);
                if (producto == null)
                {
                    throw new InvalidOperationException("El producto seleccionado no pertenece al cliente hijo seleccionado.");
                }
            }

            var productoNombre = producto?.Nombre ?? (input.ProductoManual ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(productoNombre))
            {
                throw new InvalidOperationException("Selecciona un producto existente o escribe el producto en la opción Otro.");
            }

            var fechaAprovisionamiento = DateTime.SpecifyKind(input.FechaAprovisionamiento.Date, DateTimeKind.Unspecified);
            var detalle = BuildSolicitudDetalle(
                enterprise,
                clienteHijo,
                producto,
                productoNombre,
                solicitante,
                input.Cantidad,
                fechaAprovisionamiento,
                MovimientoNuevaLicencia);
            var solicitud = new Entity(SolicitudEntity)
            {
                [SolicitudNombre] = $"{clienteHijo.ClienteNombre} - {productoNombre} - {input.Cantidad}",
                [SolicitudCliente] = new EntityReference(ClienteEntity, clienteHijo.ClienteId),
                [SolicitudProducto] = productoNombre,
                [SolicitudCantidad] = input.Cantidad,
                [SolicitudEstado] = new OptionSetValue(SolicitudEstadoPendiente),
                [SolicitudFecha] = fechaAprovisionamiento,
                [SolicitudDetalle] = detalle,
                [SolicitudValorUnitario] = producto?.PrecioUnitarioUsd ?? 0m
            };

            if (schema.SolicitudTipoMovimientoReady)
            {
                solicitud[SolicitudTipoMovimiento] = MovimientoNuevaLicencia;
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady && producto != null)
            {
                solicitud[SolicitudRegistroProductoCloud] = new EntityReference(ProductoCloudEntity, producto.SalesRecordId);
            }

            if (schema.SolicitadoPorReady)
            {
                solicitud[SolicitudSolicitadoPor] = solicitante;
            }

            if (schema.SolicitadoPorCorreoReady)
            {
                solicitud[SolicitudSolicitadoPorCorreo] = solicitante;
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

        private async Task<Guid> DesasignarLicenciasAsync(
            IReadOnlyList<string> candidateEmails,
            CrearSolicitudClienteVm input,
            string solicitante)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para desasignar licencias.");
            }

            if (input.Cantidad <= 0)
            {
                throw new InvalidOperationException("La cantidad a desasignar debe ser mayor a cero.");
            }

            if (!input.SalesRecordId.HasValue || input.SalesRecordId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("Selecciona el producto que quieres desasignar.");
            }

            if (input.FechaAprovisionamiento == default)
            {
                input.FechaAprovisionamiento = DateTime.Today;
            }

            var schema = await EnsureSchemaAsync();
            if (!schema.SolicitudEntityReady || !schema.SolicitudClienteLookupReady)
            {
                throw new InvalidOperationException("La tabla solicitudesclientes no está lista en Dataverse.");
            }

            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var clienteHijoId = input.ClienteHijoId != Guid.Empty ? input.ClienteHijoId : clienteId;
            if (!enterprise.ChildrenById.TryGetValue(clienteHijoId, out var clienteHijo))
            {
                throw new InvalidOperationException("El cliente hijo seleccionado no pertenece al grupo empresarial visible para este usuario.");
            }

            var producto = await GetProductoCloudAsync(input.SalesRecordId.Value, clienteHijoId, enterprise.ChildrenById);
            if (producto == null)
            {
                throw new InvalidOperationException("El producto seleccionado no pertenece al cliente hijo seleccionado.");
            }

            if (producto.Cantidad < input.Cantidad)
            {
                throw new InvalidOperationException($"No puedes desasignar {input.Cantidad} licencias; {producto.ClienteNombre} solo tiene {producto.Cantidad} disponibles en {producto.Nombre}.");
            }

            var fechaMovimiento = DateTime.SpecifyKind(input.FechaAprovisionamiento.Date, DateTimeKind.Unspecified);
            var detalle = BuildSolicitudDetalle(
                enterprise,
                clienteHijo,
                producto,
                producto.Nombre,
                solicitante,
                input.Cantidad,
                fechaMovimiento,
                MovimientoDesasignacion);

            var solicitud = new Entity(SolicitudEntity)
            {
                [SolicitudNombre] = $"{clienteHijo.ClienteNombre} - Desasignacion - {producto.Nombre} - {input.Cantidad}",
                [SolicitudCliente] = new EntityReference(ClienteEntity, clienteHijo.ClienteId),
                [SolicitudProducto] = producto.Nombre,
                [SolicitudCantidad] = input.Cantidad,
                [SolicitudEstado] = new OptionSetValue(SolicitudEstadoAprovisionado),
                [SolicitudFecha] = fechaMovimiento,
                [SolicitudDetalle] = detalle,
                [SolicitudValorUnitario] = producto.PrecioUnitarioUsd
            };

            if (schema.SolicitudTipoMovimientoReady)
            {
                solicitud[SolicitudTipoMovimiento] = MovimientoDesasignacion;
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                solicitud[SolicitudRegistroProductoCloud] = new EntityReference(ProductoCloudEntity, producto.SalesRecordId);
            }

            if (schema.SolicitadoPorReady)
            {
                solicitud[SolicitudSolicitadoPor] = solicitante;
            }

            if (schema.SolicitadoPorCorreoReady)
            {
                solicitud[SolicitudSolicitadoPorCorreo] = solicitante;
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

            var request = new ExecuteTransactionRequest
            {
                ReturnResponses = false,
                Requests = new OrganizationRequestCollection
                {
                    new UpdateRequest
                    {
                        Target = new Entity(ProductoCloudEntity, producto.SalesRecordId)
                        {
                            [ProductoCloudCantidad] = producto.Cantidad - input.Cantidad
                        }
                    },
                    new CreateRequest
                    {
                        Target = solicitud
                    }
                }
            };

            await _svc.ExecuteAsync(request);
            return clienteId;
        }

        public async Task<Guid> AsignarLicenciasSinAsignarAsync(
            IReadOnlyList<string> candidateEmails,
            AsignarLicenciasSinAsignarVm input,
            string solicitante)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para asignar licencias.");
            }

            if (input.Cantidad <= 0)
            {
                throw new InvalidOperationException("La cantidad a asignar debe ser mayor a cero.");
            }

            var productoKey = NormalizeProductGroupKey(input.ProductoKey, input.ProductoLogicalName, input.ProductoId, input.Producto);
            if (string.IsNullOrWhiteSpace(productoKey))
            {
                throw new InvalidOperationException("No se pudo identificar el producto a asignar.");
            }

            var schema = await EnsureSchemaAsync();
            if (!schema.SolicitudEntityReady || !schema.SolicitudClienteLookupReady)
            {
                throw new InvalidOperationException("La tabla solicitudesclientes no está lista en Dataverse.");
            }

            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            if (!enterprise.ChildrenById.TryGetValue(input.DestinoClienteId, out var clienteDestino))
            {
                throw new InvalidOperationException("El cliente destino no pertenece al grupo empresarial visible para este usuario.");
            }

            var productos = await GetProductosCloudAsync(enterprise.ChildrenById.Keys.ToList(), enterprise.ChildrenById, includeZero: true);
            var productosPorId = productos.ToDictionary(p => p.SalesRecordId);
            var solicitudes = await GetSolicitudesAsync(
                enterprise.ChildrenById.Keys.ToList(),
                schema,
                productosPorId,
                enterprise.ChildrenById,
                enterprise.GrupoEmpresarialNombre);
            var licenciasSinAsignar = BuildLicenciasSinAsignar(solicitudes);
            var licenciaLibre = licenciasSinAsignar.FirstOrDefault(l =>
                string.Equals(l.ProductoKey, productoKey, StringComparison.OrdinalIgnoreCase) &&
                l.PrecioUnitarioUsd == input.PrecioUnitarioUsd);

            if (licenciaLibre == null || licenciaLibre.Cantidad < input.Cantidad)
            {
                var disponibles = licenciaLibre?.Cantidad ?? 0;
                throw new InvalidOperationException($"No puedes asignar {input.Cantidad} licencias; solo hay {disponibles} sin asignar para {input.Producto}.");
            }

            var productoDestino = productos.FirstOrDefault(p =>
                p.ClienteId == clienteDestino.ClienteId &&
                string.Equals(GetProductGroupKey(p), productoKey, StringComparison.OrdinalIgnoreCase) &&
                p.PrecioUnitarioUsd == input.PrecioUnitarioUsd);

            var productoNombre = !string.IsNullOrWhiteSpace(licenciaLibre.Producto)
                ? licenciaLibre.Producto
                : input.Producto.Trim();
            var productoLogicalName = !string.IsNullOrWhiteSpace(licenciaLibre.ProductoLogicalName)
                ? licenciaLibre.ProductoLogicalName
                : input.ProductoLogicalName;
            var productoId = licenciaLibre.ProductoId ?? input.ProductoId;
            var diaFacturacion = licenciaLibre.DiaFacturacion ?? input.DiaFacturacion;
            var productoReference = productoId.HasValue && !string.IsNullOrWhiteSpace(productoLogicalName)
                ? new EntityReference(productoLogicalName, productoId.Value)
                : null;

            var fechaMovimiento = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
            var productoMovimiento = productoDestino ?? new ProductoCloudDto(
                Guid.NewGuid(),
                clienteDestino.ClienteId,
                clienteDestino.ClienteNombre,
                clienteDestino.AccountIds,
                productoNombre,
                0,
                input.PrecioUnitarioUsd,
                diaFacturacion,
                productoReference);

            var detalle = BuildSolicitudDetalle(
                enterprise,
                clienteDestino,
                productoMovimiento,
                productoNombre,
                solicitante,
                input.Cantidad,
                fechaMovimiento,
                MovimientoAsignacionDisponible);

            var solicitud = new Entity(SolicitudEntity)
            {
                [SolicitudNombre] = $"{clienteDestino.ClienteNombre} - Asignacion - {productoNombre} - {input.Cantidad}",
                [SolicitudCliente] = new EntityReference(ClienteEntity, clienteDestino.ClienteId),
                [SolicitudProducto] = productoNombre,
                [SolicitudCantidad] = input.Cantidad,
                [SolicitudEstado] = new OptionSetValue(SolicitudEstadoAprovisionado),
                [SolicitudFecha] = fechaMovimiento,
                [SolicitudDetalle] = detalle,
                [SolicitudValorUnitario] = input.PrecioUnitarioUsd
            };

            if (schema.SolicitudTipoMovimientoReady)
            {
                solicitud[SolicitudTipoMovimiento] = MovimientoAsignacionDisponible;
            }

            if (schema.SolicitudRegistroProductoCloudLookupReady)
            {
                solicitud[SolicitudRegistroProductoCloud] = new EntityReference(ProductoCloudEntity, productoMovimiento.SalesRecordId);
            }

            if (schema.SolicitadoPorReady)
            {
                solicitud[SolicitudSolicitadoPor] = solicitante;
            }

            if (schema.SolicitadoPorCorreoReady)
            {
                solicitud[SolicitudSolicitadoPorCorreo] = solicitante;
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
                solicitud[SolicitudClienteHijoName] = clienteDestino.ClienteNombre;
            }

            if (schema.SolicitudAccountIdsReady)
            {
                solicitud[SolicitudAccountIds] = string.Join(", ", clienteDestino.AccountIds);
            }

            var request = new ExecuteTransactionRequest
            {
                ReturnResponses = false,
                Requests = new OrganizationRequestCollection()
            };

            if (productoDestino == null)
            {
                var productoNuevo = new Entity(ProductoCloudEntity, productoMovimiento.SalesRecordId)
                {
                    [ProductoCloudPrimaryName] = $"{clienteDestino.ClienteNombre} - {productoNombre}",
                    [ProductoCloudCliente] = new EntityReference(ClienteEntity, clienteDestino.ClienteId),
                    [ProductoCloudNombre] = productoNombre,
                    [ProductoCloudCantidad] = input.Cantidad,
                    [ProductoCloudPrecioDecimal] = input.PrecioUnitarioUsd
                };

                if (productoReference != null)
                {
                    productoNuevo[ProductoCloudProducto] = productoReference;
                }

                if (diaFacturacion.HasValue)
                {
                    productoNuevo[ProductoCloudDiaFacturacion] = diaFacturacion.Value;
                }

                request.Requests.Add(new CreateRequest { Target = productoNuevo });
            }
            else
            {
                request.Requests.Add(new UpdateRequest
                {
                    Target = new Entity(ProductoCloudEntity, productoDestino.SalesRecordId)
                    {
                        [ProductoCloudCantidad] = productoDestino.Cantidad + input.Cantidad
                    }
                });
            }

            request.Requests.Add(new CreateRequest { Target = solicitud });
            await _svc.ExecuteAsync(request);
            return clienteId;
        }

        public async Task<Guid> ActualizarFechaCorteAsync(IReadOnlyList<string> candidateEmails, ActualizarFechaCorteLicenciamientoVm input)
        {
            EnsureCanEditStructure(candidateEmails);
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

        public async Task<Guid> MoverLicenciasAsync(IReadOnlyList<string> candidateEmails, MoverLicenciasVm input)
        {
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No tienes un cliente asociado para mover licencias.");
            }

            if (input.Cantidad <= 0)
            {
                throw new InvalidOperationException("La cantidad a mover debe ser mayor a cero.");
            }

            if (input.OrigenClienteId == Guid.Empty || input.DestinoClienteId == Guid.Empty || input.OrigenClienteId == input.DestinoClienteId)
            {
                throw new InvalidOperationException("Selecciona un cliente origen y un cliente destino diferentes.");
            }

            var schema = await EnsureSchemaAsync();
            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            if (!enterprise.ChildrenById.ContainsKey(input.OrigenClienteId) || !enterprise.ChildrenById.ContainsKey(input.DestinoClienteId))
            {
                throw new InvalidOperationException("Solo puedes mover licencias entre clientes hijos del grupo empresarial visible.");
            }

            var productos = await GetProductosCloudAsync(enterprise.ChildrenById.Keys.ToList(), enterprise.ChildrenById);
            var origen = productos.FirstOrDefault(p => p.SalesRecordId == input.OrigenSalesRecordId && p.ClienteId == input.OrigenClienteId);
            if (origen == null)
            {
                throw new InvalidOperationException("El producto origen no pertenece al cliente hijo seleccionado.");
            }

            if (origen.Cantidad < input.Cantidad)
            {
                throw new InvalidOperationException($"No puedes mover {input.Cantidad} licencias; {origen.ClienteNombre} solo tiene {origen.Cantidad} disponibles en {origen.Nombre}.");
            }

            var destino = productos.FirstOrDefault(p =>
                p.ClienteId == input.DestinoClienteId &&
                IsSameProductAndPrice(origen, p));

            if (destino == null)
            {
                throw new InvalidOperationException("El cliente destino no tiene un registro de producto cloud con el mismo producto y el mismo precio unitario.");
            }

            var request = new ExecuteTransactionRequest
            {
                ReturnResponses = false,
                Requests = new OrganizationRequestCollection()
            };

            request.Requests.Add(new UpdateRequest
            {
                Target = new Entity(ProductoCloudEntity, origen.SalesRecordId)
                {
                    [ProductoCloudCantidad] = origen.Cantidad - input.Cantidad
                }
            });

            request.Requests.Add(new UpdateRequest
            {
                Target = new Entity(ProductoCloudEntity, destino.SalesRecordId)
                {
                    [ProductoCloudCantidad] = destino.Cantidad + input.Cantidad
                }
            });

            await _svc.ExecuteAsync(request);
            return clienteId;
        }

        public async Task<Guid> ActualizarGrupoEmpresarialAsync(IReadOnlyList<string> candidateEmails, ActualizarGrupoEmpresarialVm input)
        {
            EnsureCanEditStructure(candidateEmails);
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No hay cliente base para actualizar el grupo.");
            }

            var groupName = (input.GrupoEmpresarialNombre ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                throw new InvalidOperationException("Escribe el nombre del grupo empresarial.");
            }

            var schema = await EnsureSchemaAsync();
            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var rows = enterprise.AccountRows.Where(r => r.Id != Guid.Empty).ToList();
            if (rows.Count == 0)
            {
                rows = await GetAccountIdRowsByClienteAsync(clienteId, schema);
            }

            if (rows.Count == 0)
            {
                throw new InvalidOperationException("El cliente seleccionado no tiene Account IDs para asociar al grupo.");
            }

            var groupId = NormalizeGroupId(input.GrupoEmpresarialId, enterprise.GrupoEmpresarialId);
            await UpdateAccountRowsGroupAsync(rows.Select(r => r.Id), groupId, groupName, schema);
            return clienteId;
        }

        public async Task<Guid> AsignarAccountIdAGrupoAsync(IReadOnlyList<string> candidateEmails, AsignarAccountIdGrupoVm input)
        {
            EnsureCanEditStructure(candidateEmails);
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No hay cliente base para asignar Account IDs.");
            }

            if (input.AccountIdRowId == Guid.Empty)
            {
                throw new InvalidOperationException("Selecciona el Account ID que quieres asignar al grupo.");
            }

            var schema = await EnsureSchemaAsync();
            var clienteNombre = await GetClienteNombreAsync(clienteId);
            var enterprise = await GetEnterpriseContextAsync(clienteId, clienteNombre, schema);
            var groupName = !string.IsNullOrWhiteSpace(input.GrupoEmpresarialNombre)
                ? input.GrupoEmpresarialNombre.Trim()
                : enterprise.GrupoEmpresarialNombre;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                throw new InvalidOperationException("Primero define el nombre del grupo empresarial.");
            }

            var accountRow = await GetAccountIdRowAsync(input.AccountIdRowId, schema);
            if (accountRow == null)
            {
                throw new InvalidOperationException("No encontré el Account ID seleccionado.");
            }

            var groupId = NormalizeGroupId(input.GrupoEmpresarialId, enterprise.GrupoEmpresarialId);
            var rowsToUpdate = new List<Guid> { accountRow.Id };
            if (string.IsNullOrWhiteSpace(enterprise.GrupoEmpresarialId))
            {
                rowsToUpdate.AddRange(enterprise.AccountRows.Select(r => r.Id));
            }

            await UpdateAccountRowsGroupAsync(rowsToUpdate, groupId, groupName, schema);
            return clienteId;
        }

        public async Task<Guid> QuitarAccountIdDelGrupoAsync(IReadOnlyList<string> candidateEmails, QuitarAccountIdGrupoVm input)
        {
            EnsureCanEditStructure(candidateEmails);
            var clienteId = await ResolveAuthorizedClientIdAsync(candidateEmails, input.ClienteId, null);
            if (clienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("No hay cliente base para modificar el grupo.");
            }

            if (input.AccountIdRowId == Guid.Empty)
            {
                throw new InvalidOperationException("Selecciona el Account ID que quieres quitar del grupo.");
            }

            var schema = await EnsureSchemaAsync();
            var entity = new Entity(AccountIdEntity, input.AccountIdRowId);
            if (schema.AccountIdGrupoEmpresarialIdReady)
            {
                entity[AccountIdGrupoEmpresarialId] = null;
            }

            if (schema.AccountIdGrupoEmpresarialNameReady)
            {
                entity[AccountIdGrupoEmpresarialName] = null;
            }

            await _svc.UpdateAsync(entity);
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
                AccountRows = rows,
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

        private async Task<List<AccountIdRowDto>> GetAllAccountIdRowsAsync(LicenciamientoSchemaStatus schema)
        {
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
            query.AddOrder(AccountIdName, OrderType.Ascending);

            var result = await _svc.RetrieveMultipleAsync(query);
            var rows = result.Entities.Select(e => ToAccountIdRowDto(e, schema)).Where(r => r.ClienteId != Guid.Empty).ToList();
            return await HydrateAccountRowsAsync(rows, Guid.Empty, string.Empty);
        }

        private async Task<AccountIdRowDto?> GetAccountIdRowAsync(Guid accountIdRowId, LicenciamientoSchemaStatus schema)
        {
            try
            {
                var entity = await _svc.RetrieveAsync(AccountIdEntity, accountIdRowId, new ColumnSet(GetAccountIdColumns(schema).ToArray()));
                var rows = await HydrateAccountRowsAsync(new List<AccountIdRowDto> { ToAccountIdRowDto(entity, schema) }, Guid.Empty, string.Empty);
                return rows.FirstOrDefault(r => r.ClienteId != Guid.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer el Account ID {AccountIdRowId}", accountIdRowId);
                return null;
            }
        }

        private async Task UpdateAccountRowsGroupAsync(IEnumerable<Guid> accountRowIds, string groupId, string groupName, LicenciamientoSchemaStatus schema)
        {
            var ids = accountRowIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            foreach (var id in ids)
            {
                var entity = new Entity(AccountIdEntity, id);
                if (schema.AccountIdGrupoEmpresarialIdReady)
                {
                    entity[AccountIdGrupoEmpresarialId] = groupId;
                }

                if (schema.AccountIdGrupoEmpresarialNameReady)
                {
                    entity[AccountIdGrupoEmpresarialName] = groupName;
                }

                await _svc.UpdateAsync(entity);
            }
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

        private static AccountIdLicenciamientoVm ToAccountIdLicenciamientoVm(AccountIdRowDto row)
        {
            return new AccountIdLicenciamientoVm
            {
                Id = row.Id,
                AccountId = row.AccountId,
                ClienteId = row.ClienteId,
                ClienteNombre = row.ClienteNombre,
                GrupoEmpresarialId = row.GrupoEmpresarialId,
                GrupoEmpresarialNombre = row.GrupoEmpresarialName
            };
        }

        private async Task<List<ProductoCloudDto>> GetProductosCloudAsync(
            IReadOnlyCollection<Guid> clienteIds,
            IReadOnlyDictionary<Guid, ChildClienteDto> childrenById,
            bool includeZero = false)
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
                .Where(p => p.SalesRecordId != Guid.Empty && p.ClienteId != Guid.Empty && (includeZero || p.Cantidad > 0))
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
                SolicitudDetalle,
                SolicitudValorUnitario,
                "createdon"
            };

            if (schema.SolicitudProductoReady)
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

            if (schema.SolicitadoPorCorreoReady)
            {
                columns.Add(SolicitudSolicitadoPorCorreo);
            }

            if (schema.SolicitudGrupoEmpresarialNameReady)
            {
                columns.Add(SolicitudGrupoEmpresarialName);
            }

            if (schema.SolicitudClienteHijoNameReady)
            {
                columns.Add(SolicitudClienteHijoName);
            }

            if (schema.SolicitudTipoMovimientoReady)
            {
                columns.Add(SolicitudTipoMovimiento);
            }

            var query = new QueryExpression(SolicitudEntity)
            {
                ColumnSet = new ColumnSet(columns.ToArray())
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
                var productoLogicalName = producto?.ProductoReference?.LogicalName ?? TryReadValue(detalle, "ProductoLogicalName") ?? string.Empty;
                var productoId = producto?.ProductoReference?.Id ?? TryReadGuid(detalle, "ProductoId");
                var productKey = producto != null
                    ? GetProductGroupKey(producto)
                    : NormalizeProductGroupKey(
                        TryReadValue(detalle, "ProductoKey"),
                        productoLogicalName,
                        productoId == Guid.Empty ? null : productoId,
                        TryReadValue(detalle, "Producto"));
                var diaFacturacion = producto?.DiaFacturacion ?? TryReadInt(detalle, "DiaFacturacion");

                var productoTexto = schema.SolicitudProductoReady
                    ? e.GetAttributeValue<string>(SolicitudProducto)
                    : null;
                var productoNombre = producto?.Nombre
                    ?? productoTexto
                    ?? TryReadValue(detalle, "Producto")
                    ?? e.GetAttributeValue<string>(SolicitudNombre)
                    ?? "Producto";

                var solicitadoPor = schema.SolicitadoPorReady
                    ? e.GetAttributeValue<string>(SolicitudSolicitadoPor)
                    : null;

                var solicitadoPorCorreo = schema.SolicitadoPorCorreoReady
                    ? e.GetAttributeValue<string>(SolicitudSolicitadoPorCorreo)
                    : null;

                var clienteNombre = schema.SolicitudClienteHijoNameReady
                    ? e.GetAttributeValue<string>(SolicitudClienteHijoName)
                    : null;

                var grupoNombre = schema.SolicitudGrupoEmpresarialNameReady
                    ? e.GetAttributeValue<string>(SolicitudGrupoEmpresarialName)
                    : null;
                var tipoMovimiento = schema.SolicitudTipoMovimientoReady
                    ? e.GetAttributeValue<string>(SolicitudTipoMovimiento)
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
                    e.GetAttributeValue<DateTime?>("createdon"),
                    e.GetAttributeValue<DateTime?>(SolicitudFecha),
                    e.GetAttributeValue<OptionSetValue>(SolicitudEstado)?.Value ?? SolicitudEstadoPendiente,
                    solicitadoPor ?? solicitadoPorCorreo ?? TryReadValue(detalle, "SolicitadoPor") ?? string.Empty,
                    NormalizeTipoMovimiento(tipoMovimiento ?? TryReadValue(detalle, "TipoMovimiento")),
                    productKey,
                    productoLogicalName,
                    productoId == Guid.Empty ? null : productoId,
                    diaFacturacion);
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
                        "cr07a_cliente_solicitudesclientes");

                    status.SolicitudProductoReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudProducto,
                        "Producto",
                        250);

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

                    status.SolicitudDetalleReady = await EnsureMemoAttributeAsync(
                        SolicitudEntity,
                        SolicitudDetalle,
                        "Detalle",
                        4000);

                    status.SolicitudValorUnitarioReady = await EnsureDecimalAttributeAsync(
                        SolicitudEntity,
                        SolicitudValorUnitario,
                        "Valor unitario",
                        0m,
                        100000000m,
                        2);

                    status.SolicitudRegistroProductoCloudLookupReady = await EnsureLookupAttributeAsync(
                        SolicitudEntity,
                        SolicitudRegistroProductoCloud,
                        "Registro producto cloud",
                        ProductoCloudEntity,
                        "cr07a_solicitudesclientes_registroproductocloud");

                    status.SolicitadoPorReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudSolicitadoPor,
                        "Solicitado por",
                        200);

                    status.SolicitadoPorCorreoReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudSolicitadoPorCorreo,
                        "Solicitado por correo",
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

                    status.SolicitudAprobadoPorReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudAprobadoPor,
                        "Aprobado por",
                        200);

                    status.SolicitudFechaAprobacionReady = await EnsureDateTimeAttributeAsync(
                        SolicitudEntity,
                        SolicitudFechaAprobacion,
                        "Fecha aprobación");

                    status.SolicitudTipoMovimientoReady = await EnsureStringAttributeAsync(
                        SolicitudEntity,
                        SolicitudTipoMovimiento,
                        "Tipo movimiento",
                        80);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo asegurar todo el esquema de licenciamiento en Dataverse.");
                    status.SetupError = "No se pudo crear o validar todo el esquema de licenciamiento en Dataverse. Revisa permisos de personalización del usuario/aplicación.";
                    status.AccountIdGrupoEmpresarialIdReady = await AttributeExistsAsync(AccountIdEntity, AccountIdGrupoEmpresarialId);
                    status.AccountIdGrupoEmpresarialNameReady = await AttributeExistsAsync(AccountIdEntity, AccountIdGrupoEmpresarialName);
                    status.SolicitudEntityReady = await EntityExistsAsync(SolicitudEntity);
                    status.SolicitudClienteLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudCliente);
                    status.SolicitudProductoReady = await AttributeExistsAsync(SolicitudEntity, SolicitudProducto);
                    status.SolicitudRegistroProductoCloudLookupReady = await AttributeExistsAsync(SolicitudEntity, SolicitudRegistroProductoCloud);
                    status.SolicitadoPorReady = await AttributeExistsAsync(SolicitudEntity, SolicitudSolicitadoPor);
                    status.SolicitadoPorCorreoReady = await AttributeExistsAsync(SolicitudEntity, SolicitudSolicitadoPorCorreo);
                    status.SolicitudGrupoEmpresarialIdReady = await AttributeExistsAsync(SolicitudEntity, SolicitudGrupoEmpresarialId);
                    status.SolicitudGrupoEmpresarialNameReady = await AttributeExistsAsync(SolicitudEntity, SolicitudGrupoEmpresarialName);
                    status.SolicitudClienteHijoNameReady = await AttributeExistsAsync(SolicitudEntity, SolicitudClienteHijoName);
                    status.SolicitudAccountIdsReady = await AttributeExistsAsync(SolicitudEntity, SolicitudAccountIds);
                    status.SolicitudAprobadoPorReady = await AttributeExistsAsync(SolicitudEntity, SolicitudAprobadoPor);
                    status.SolicitudFechaAprobacionReady = await AttributeExistsAsync(SolicitudEntity, SolicitudFechaAprobacion);
                    status.SolicitudTipoMovimientoReady = await AttributeExistsAsync(SolicitudEntity, SolicitudTipoMovimiento);
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
                    SchemaName = SolicitudEntitySchemaName,
                    DisplayName = Label("solicitudesclientes"),
                    DisplayCollectionName = Label("solicitudesclientes"),
                    Description = Label("Solicitudes de aprovisionamiento de licencias asociadas al cliente hijo para prorrateo mensual."),
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
                            new OptionMetadata(Label("Rechazado"), SolicitudEstadoRechazado)
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

        private static void EnsureCanEditStructure(IReadOnlyList<string> candidateEmails)
        {
            if (!IsAdminLicenciamiento(candidateEmails))
            {
                throw new UnauthorizedAccessException("Solo sruiz@digitaltechcolombia.com puede asignar hijos o modificar relaciones de grupo empresarial.");
            }
        }

        private static bool IsSolicitudFacturable(int estado)
        {
            return estado == SolicitudEstadoAprovisionado;
        }

        private static string EstadoSolicitudLabel(int estado)
        {
            return estado switch
            {
                SolicitudEstadoAprovisionado => "Aprovisionado",
                SolicitudEstadoRechazado => "Rechazado",
                _ => "Pendiente"
            };
        }

        private static string NormalizeTipoMovimiento(string? tipo)
        {
            if (string.Equals(tipo, MovimientoDesasignacion, StringComparison.OrdinalIgnoreCase))
            {
                return MovimientoDesasignacion;
            }

            if (string.Equals(tipo, MovimientoAsignacionDisponible, StringComparison.OrdinalIgnoreCase))
            {
                return MovimientoAsignacionDisponible;
            }

            return MovimientoNuevaLicencia;
        }

        private static bool IsNuevaLicencia(string? tipo)
            => string.Equals(NormalizeTipoMovimiento(tipo), MovimientoNuevaLicencia, StringComparison.OrdinalIgnoreCase);

        private static bool IsDesasignacion(string? tipo)
            => string.Equals(NormalizeTipoMovimiento(tipo), MovimientoDesasignacion, StringComparison.OrdinalIgnoreCase);

        private static bool IsAsignacionDisponible(string? tipo)
            => string.Equals(NormalizeTipoMovimiento(tipo), MovimientoAsignacionDisponible, StringComparison.OrdinalIgnoreCase);

        private static string TipoMovimientoLabel(string? tipo)
        {
            return NormalizeTipoMovimiento(tipo) switch
            {
                MovimientoDesasignacion => "Desasignacion",
                MovimientoAsignacionDisponible => "Asignacion libre",
                _ => "Solicitud nueva"
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

        private static string NormalizeProductGroupKey(string? requestedKey, string? logicalName, Guid? productoId, string? productoNombre)
        {
            if (!string.IsNullOrWhiteSpace(requestedKey))
            {
                return requestedKey.Trim();
            }

            if (productoId.HasValue && productoId.Value != Guid.Empty && !string.IsNullOrWhiteSpace(logicalName))
            {
                return $"{logicalName}:{productoId.Value:D}";
            }

            return (productoNombre ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool IsSameProductAndPrice(ProductoCloudDto left, ProductoCloudDto right)
        {
            return string.Equals(GetProductGroupKey(left), GetProductGroupKey(right), StringComparison.OrdinalIgnoreCase)
                && left.PrecioUnitarioUsd == right.PrecioUnitarioUsd;
        }

        private static string NormalizeGroupId(string? requestedGroupId, string? currentGroupId)
        {
            if (!string.IsNullOrWhiteSpace(requestedGroupId))
            {
                return requestedGroupId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(currentGroupId))
            {
                return currentGroupId.Trim();
            }

            return Guid.NewGuid().ToString("N");
        }

        private static string BuildSolicitudDetalle(
            EnterpriseContextDto enterprise,
            ChildClienteDto clienteHijo,
            ProductoCloudDto? producto,
            string productoNombre,
            string solicitante,
            int cantidad,
            DateTime fechaAprovisionamiento,
            string tipoMovimiento)
        {
            var salesRecordId = producto?.SalesRecordId.ToString("D") ?? string.Empty;
            var productoReference = producto?.ProductoReference;
            var productoId = productoReference?.Id.ToString("D") ?? string.Empty;
            var productoLogicalName = productoReference?.LogicalName ?? string.Empty;
            var productoKey = producto != null
                ? GetProductGroupKey(producto)
                : NormalizeProductGroupKey(null, productoLogicalName, productoReference?.Id, productoNombre);
            return string.Join(";",
                "PortalLicenciamiento",
                $"TipoMovimiento={EscapeDetail(NormalizeTipoMovimiento(tipoMovimiento))}",
                $"GrupoEmpresarialId={EscapeDetail(enterprise.GrupoEmpresarialId)}",
                $"GrupoEmpresarial={EscapeDetail(enterprise.GrupoEmpresarialNombre)}",
                $"ClienteHijoId={clienteHijo.ClienteId:D}",
                $"ClienteHijo={EscapeDetail(clienteHijo.ClienteNombre)}",
                $"AccountIds={EscapeDetail(string.Join(",", clienteHijo.AccountIds))}",
                $"SalesRecordId={salesRecordId}",
                $"ProductoKey={EscapeDetail(productoKey)}",
                $"ProductoLogicalName={EscapeDetail(productoLogicalName)}",
                $"ProductoId={productoId}",
                $"Producto={EscapeDetail(productoNombre)}",
                $"PrecioUnitarioUsd={(producto?.PrecioUnitarioUsd ?? 0m).ToString(CultureInfo.InvariantCulture)}",
                $"DiaFacturacion={(producto?.DiaFacturacion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"SolicitadoPor={EscapeDetail(solicitante)}",
                $"FechaAprovisionamiento={fechaAprovisionamiento:yyyy-MM-dd}",
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

        private static int? TryReadInt(string detail, string key)
        {
            var raw = TryReadValue(detail, key);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private static List<LicenciaSinAsignarVm> BuildLicenciasSinAsignar(IEnumerable<SolicitudDto> solicitudes)
        {
            var libres = new Dictionary<string, LicenciaSinAsignarVm>(StringComparer.OrdinalIgnoreCase);

            foreach (var solicitud in solicitudes.Where(s => IsSolicitudFacturable(s.EstadoValor)))
            {
                if (!IsDesasignacion(solicitud.TipoMovimiento) && !IsAsignacionDisponible(solicitud.TipoMovimiento))
                {
                    continue;
                }

                var productKey = NormalizeProductGroupKey(
                    solicitud.ProductoKey,
                    solicitud.ProductoLogicalName,
                    solicitud.ProductoId,
                    solicitud.ProductoNombre);
                if (string.IsNullOrWhiteSpace(productKey))
                {
                    continue;
                }

                var key = $"{productKey}|{solicitud.PrecioUnitarioUsd.ToString(CultureInfo.InvariantCulture)}";
                if (!libres.TryGetValue(key, out var libre))
                {
                    libre = new LicenciaSinAsignarVm
                    {
                        ProductoKey = productKey,
                        ProductoId = solicitud.ProductoId,
                        ProductoLogicalName = solicitud.ProductoLogicalName,
                        Producto = solicitud.ProductoNombre,
                        PrecioUnitarioUsd = solicitud.PrecioUnitarioUsd,
                        DiaFacturacion = solicitud.DiaFacturacion
                    };
                    libres[key] = libre;
                }

                if (IsDesasignacion(solicitud.TipoMovimiento))
                {
                    libre.Cantidad += solicitud.Cantidad;
                }
                else
                {
                    libre.Cantidad -= solicitud.Cantidad;
                }

                if (string.IsNullOrWhiteSpace(libre.Producto) && !string.IsNullOrWhiteSpace(solicitud.ProductoNombre))
                {
                    libre.Producto = solicitud.ProductoNombre;
                }

                if (!libre.ProductoId.HasValue && solicitud.ProductoId.HasValue)
                {
                    libre.ProductoId = solicitud.ProductoId;
                    libre.ProductoLogicalName = solicitud.ProductoLogicalName;
                }

                libre.DiaFacturacion ??= solicitud.DiaFacturacion;
            }

            return libres.Values
                .Where(l => l.Cantidad > 0)
                .OrderBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.PrecioUnitarioUsd)
                .ToList();
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
            DateTime? FechaAprovisionamiento,
            int EstadoValor,
            string SolicitadoPor,
            string TipoMovimiento,
            string ProductoKey,
            string ProductoLogicalName,
            Guid? ProductoId,
            int? DiaFacturacion);

        private sealed class EnterpriseContextDto
        {
            public string GrupoEmpresarialId { get; set; } = string.Empty;
            public string GrupoEmpresarialNombre { get; set; } = string.Empty;
            public bool TieneGrupoEmpresarial { get; set; }
            public List<AccountIdRowDto> AccountRows { get; set; } = new();
            public List<ChildClienteDto> Children { get; set; } = new();
            public Dictionary<Guid, ChildClienteDto> ChildrenById { get; set; } = new();
        }

        private sealed class LicenciamientoSchemaStatus
        {
            public bool AccountIdGrupoEmpresarialIdReady { get; set; }
            public bool AccountIdGrupoEmpresarialNameReady { get; set; }
            public bool SolicitudEntityReady { get; set; }
            public bool SolicitudClienteLookupReady { get; set; }
            public bool SolicitudProductoReady { get; set; }
            public bool SolicitudCantidadReady { get; set; }
            public bool SolicitudEstadoReady { get; set; }
            public bool SolicitudFechaReady { get; set; }
            public bool SolicitudDetalleReady { get; set; }
            public bool SolicitudValorUnitarioReady { get; set; }
            public bool SolicitudRegistroProductoCloudLookupReady { get; set; }
            public bool SolicitadoPorReady { get; set; }
            public bool SolicitadoPorCorreoReady { get; set; }
            public bool SolicitudGrupoEmpresarialIdReady { get; set; }
            public bool SolicitudGrupoEmpresarialNameReady { get; set; }
            public bool SolicitudClienteHijoNameReady { get; set; }
            public bool SolicitudAccountIdsReady { get; set; }
            public bool SolicitudAprobadoPorReady { get; set; }
            public bool SolicitudFechaAprobacionReady { get; set; }
            public bool SolicitudTipoMovimientoReady { get; set; }
            public string? SetupError { get; set; }
        }
    }
}
