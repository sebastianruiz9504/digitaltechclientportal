using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Domain.Dataverse;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class SoporteController : Controller
    {
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

            var clienteInfo = await _clienteService.GetClienteByEmailAsync(email);
            if (clienteInfo == null || clienteInfo.Id == Guid.Empty)
            {
                TempData["SoporteError"] = "No se pudo determinar el cliente del usuario logueado.";
                return View(vm);
            }

            try
            {
                // 2️⃣ CLOUD: filtrado y ordenado por fecha de creación DESC
                var cloudJson = await _dv.GetAsync(
                    $"cr07a_tickets?$select=cr07a_tituloticket,cr07a_descripcion,cr07a_fechacreacion,cr07a_estado,cr07a_fechadecierre" +
                    $"&$filter=_cr07a_cliente_value eq {clienteInfo.Id}" +
                    $"&$orderby=cr07a_fechacreacion desc"
                );

                vm.CloudTickets = cloudJson.RootElement.GetProperty("value")
                    .EnumerateArray()
                    .Select(e => new CloudTicketVm
                    {
                        Titulo = GetString(e, "cr07a_tituloticket"),
                        Descripcion = GetString(e, "cr07a_descripcion"),
                        FechaCreacion = GetDateTime(e, "cr07a_fechacreacion") ?? DateTime.MinValue,
                        Estado = MapEstadoCloud(GetInt(e, "cr07a_estado")),
                        FechaCierre = GetDateTime(e, "cr07a_fechadecierre")
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

        private static string MapEstadoCloud(int? code) => code switch
        {
            645250000 => "Abierto",
            645250001 => "En Proceso",
            645250002 => "Cerrado",
            _ => "Desconocido"
        };

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
    }
}