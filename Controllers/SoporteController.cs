using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Domain.Dataverse;
using DigitalTechClientPortal.Security;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    [RequireModule(PortalModuleKeys.Soporte)]
    public class SoporteController : Controller
    {
        private const string FormattedValueSuffix = "@OData.Community.Display.V1.FormattedValue";

        private readonly DataverseSoporteService _dv;
        private readonly DataverseClienteService _clienteService;

        public SoporteController(DataverseSoporteService dv, DataverseClienteService clienteService)
        {
            _dv = dv ?? throw new ArgumentNullException(nameof(dv));
            _clienteService = clienteService ?? throw new ArgumentNullException(nameof(clienteService));
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var vm = new SoporteVm
            {
                CloudTickets = new System.Collections.Generic.List<CloudTicketVm>(),
                Copiers = new System.Collections.Generic.List<CopierVm>()
            };

            // 1️⃣ Obtener email y cliente del usuario logueado
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                        ?? User.FindFirst("preferred_username")?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? User.FindFirst("emails")?.Value
                        ?? User.FindFirst("upn")?.Value;

            var clienteInfo = await _clienteService.GetClienteByEmailAsync(email ?? string.Empty);
            if (clienteInfo == null || clienteInfo.Id == Guid.Empty)
            {
                TempData["SoporteError"] = "No se pudo determinar el cliente del usuario logueado.";
                return View(vm);
            }

            try
            {
                // 2️⃣ CLOUD: filtrado y ordenado por fecha de creación DESC
                var cloudJson = await GetCloudTicketsJsonAsync(clienteInfo.Id);

                vm.CloudTickets = cloudJson.RootElement.GetProperty("value")
                    .EnumerateArray()
                    .Select(e => new CloudTicketVm
                    {
                        Id = GetRecordId(e),
                        Titulo = GetString(e, "cr07a_tituloticket"),
                        Descripcion = GetString(e, "cr07a_descripcion"),
                        FechaCreacion = GetDateTime(e, "cr07a_fechacreacion") ?? DateTime.MinValue,
                        Estado = MapEstadoCloud(GetInt(e, "cr07a_estado")),
                        FechaCierre = GetDateTime(e, "cr07a_fechadecierre"),
                        Tipo = GetDisplayString(e, "cr07a_tipo"),
                        Categoria = GetDisplayString(e, "cr07a_categoria", "_cr07a_categoria_value"),
                        HorasTomadas = GetDisplayString(e, "cr07a_horastomadas"),
                        Metodo = MapMetodoCloud(e),
                        Solucion = GetString(e, "cr07a_solucion"),
                        TieneAdjunto = HasValue(e, "cr07a_adjunto")
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TempData["SoporteError"] = $"No se pudieron cargar los tickets de Cloud. Detalle: {ex.Message}";
            }

            try
            {
                // 3️⃣ COPIERS: filtrado y ordenado por fecha de mantenimiento DESC
                var copiersJson = await _dv.GetAsync(
                    $"cr07a_mantenimientos?$select=cr07a_mantenimiento1,cr07a_fechademantenimiento,cr07a_descripciondelmantenimiento,cr07a_estadodelmantenimiento,cr07a_id" +
                    $"&$expand=cr07a_IDdeequipo($select=cr07a_nombredelequipo)" +
                    $"&$filter=_cr07a_cliente_value eq {clienteInfo.Id}" +
                    $"&$orderby=cr07a_fechademantenimiento desc"
                );

                vm.Copiers = copiersJson.RootElement.GetProperty("value")
                    .EnumerateArray()
                    .Select(e => new CopierVm
                    {
                        Nombre = GetString(e, "cr07a_mantenimiento1"),
                        IdEquipo = e.TryGetProperty("cr07a_IDdeequipo", out var eq)
                                   && eq.ValueKind == JsonValueKind.Object
                                   && eq.TryGetProperty("name", out var n)
                                   ? n.GetString() ?? string.Empty
                                   : string.Empty,
                        FechaMantenimiento = GetDateTime(e, "cr07a_fechademantenimiento") ?? DateTime.MinValue,
                        Descripcion = GetString(e, "cr07a_descripciondelmantenimiento"),
                        Estado = MapEstadoCopiers(GetInt(e, "cr07a_estadodelmantenimiento")),
                        ActaId = GetGuid(e, "cr07a_id")
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                TempData["SoporteError"] = (TempData["SoporteError"] == null)
                    ? $"No se pudieron cargar los mantenimientos de Copiers. Detalle: {ex.Message}"
                    : $"{TempData["SoporteError"]} | Copiers: {ex.Message}";
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> DescargarActa(Guid id)
        {
            var file = await _dv.GetFileAsync("cr07a_mantenimientos", id, "cr07a_actadeentregadeservicio");
            if (file == null) return NotFound();

            var contentType = string.IsNullOrWhiteSpace(file.Value.ContentType) ? "application/octet-stream" : file.Value.ContentType;
            var fileName = string.IsNullOrWhiteSpace(file.Value.FileName) ? $"acta-{id}.bin" : file.Value.FileName;
            return File(file.Value.Stream, contentType, fileName);
        }

        [HttpGet]
        public async Task<IActionResult> DescargarAdjuntoTicket(Guid id)
        {
            var file = await _dv.GetFileAsync("cr07a_tickets", id, "cr07a_adjunto");
            if (file == null) return NotFound();

            var contentType = string.IsNullOrWhiteSpace(file.Value.ContentType) ? "application/octet-stream" : file.Value.ContentType;
            var fileName = string.IsNullOrWhiteSpace(file.Value.FileName) ? $"adjunto-ticket-{id}.bin" : file.Value.FileName;
            return File(file.Value.Stream, contentType, fileName);
        }

        private async Task<JsonDocument> GetCloudTicketsJsonAsync(Guid clienteId)
        {
            const string baseSelect =
                "cr07a_tituloticket,cr07a_descripcion,cr07a_fechacreacion,cr07a_estado,cr07a_fechadecierre," +
                "cr07a_tipo,cr07a_categoria,cr07a_horastomadas,cr07a_metodo,cr07a_solucion,cr07a_adjunto";

            var query =
                $"cr07a_tickets?$select={baseSelect}" +
                $"&$filter=_cr07a_cliente_value eq {clienteId}" +
                $"&$orderby=cr07a_fechacreacion desc";

            try
            {
                return await _dv.GetAsync(query);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("cr07a_categoria", StringComparison.OrdinalIgnoreCase))
            {
                var lookupSelect = baseSelect.Replace("cr07a_categoria", "_cr07a_categoria_value", StringComparison.OrdinalIgnoreCase);
                return await _dv.GetAsync(
                    $"cr07a_tickets?$select={lookupSelect}" +
                    $"&$filter=_cr07a_cliente_value eq {clienteId}" +
                    $"&$orderby=cr07a_fechacreacion desc");
            }
        }

        private static string MapEstadoCloud(int? code) => code switch
        {
            645250000 => "Abierto",
            645250001 => "En Proceso",
            645250002 => "Cerrado",
            _ => "Desconocido"
        };

        private static string MapMetodoCloud(JsonElement e)
        {
            var code = GetInt(e, "cr07a_metodo");
            return code switch
            {
                645250000 => "Presencial",
                645250001 => "Remoto",
                _ => GetDisplayString(e, "cr07a_metodo")
            };
        }

        private static string MapEstadoCopiers(int? code) => code switch
        {
            645250000 => "Completado",
            645250001 => "Pendiente",
            _ => "Desconocido"
        };

        // Helpers parsing seguro
        private static string GetString(JsonElement e, string prop)
            => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

        private static string GetDisplayString(JsonElement e, params string[] props)
        {
            foreach (var prop in props)
            {
                if (e.TryGetProperty(prop + FormattedValueSuffix, out var formatted) &&
                    formatted.ValueKind == JsonValueKind.String)
                {
                    var value = formatted.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }

            foreach (var prop in props)
            {
                if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString() ?? string.Empty;
                }

                if (v.ValueKind == JsonValueKind.Number)
                {
                    if (v.TryGetDecimal(out var decimalValue))
                    {
                        return decimalValue.ToString("0.##", CultureInfo.InvariantCulture);
                    }

                    return v.GetRawText();
                }

                if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                {
                    return v.GetBoolean() ? "Sí" : "No";
                }
            }

            return string.Empty;
        }

        private static int? GetInt(JsonElement e, string prop)
            => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32()
                : (int?)null;

        private static DateTime? GetDateTime(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v) || (v.ValueKind != JsonValueKind.String && v.ValueKind != JsonValueKind.Number))
                return null;

            try { return v.GetDateTime(); }
            catch { return null; }
        }

        private static Guid GetGuid(JsonElement e, string prop)
        {
            if (e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (Guid.TryParse(s, out var g)) return g;
            }
            return Guid.Empty;
        }

        private static Guid GetRecordId(JsonElement e)
        {
            var id = GetGuid(e, "cr07a_ticketid");
            if (id != Guid.Empty) return id;

            id = GetGuid(e, "cr07a_ticketsid");
            if (id != Guid.Empty) return id;

            if (e.TryGetProperty("@odata.id", out var odataId) && odataId.ValueKind == JsonValueKind.String)
            {
                var raw = odataId.GetString();
                var start = raw?.LastIndexOf('(') ?? -1;
                var end = raw?.LastIndexOf(')') ?? -1;
                if (start >= 0 && end > start &&
                    Guid.TryParse(raw![(start + 1)..end], out var parsed))
                {
                    return parsed;
                }
            }

            foreach (var property in e.EnumerateObject())
            {
                if (property.Name.StartsWith("_", StringComparison.Ordinal) ||
                    !property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) ||
                    property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var raw = property.Value.GetString();
                if (Guid.TryParse(raw, out var parsed))
                {
                    return parsed;
                }
            }

            return Guid.Empty;
        }

        private static bool HasValue(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null || v.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            return v.ValueKind switch
            {
                JsonValueKind.String => !string.IsNullOrWhiteSpace(v.GetString()),
                JsonValueKind.Array => v.GetArrayLength() > 0,
                JsonValueKind.Object => v.EnumerateObject().Any(),
                _ => true
            };
        }
    }
}
