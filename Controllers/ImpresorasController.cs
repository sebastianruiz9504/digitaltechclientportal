using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class ImpresorasController : Controller
    {
        private static readonly HashSet<string> UsuariosSinFiltro = new(StringComparer.OrdinalIgnoreCase)
        {
            "germanruiz@digitaltechcolombia.com",
            "germanruiz@digitaltechcolombia",
            "sruiz@digitaltechcolombia.com",
            "sruiz@digitaltechcolombia",
            "jromero@digitaltechcolombia",
            "jromero@digitaltechcolombia.com"
        };

        private readonly DataverseSoporteService _dv;
        private readonly DataverseClienteService _clienteService;

        public ImpresorasController(DataverseSoporteService dv, DataverseClienteService clienteService)
        {
            _dv = dv ?? throw new ArgumentNullException(nameof(dv));
            _clienteService = clienteService ?? throw new ArgumentNullException(nameof(clienteService));
        }

        [HttpGet]
        public async Task<IActionResult> Index(Guid? clienteId = null)
        {
            var vm = new ImpresorasVm();

            var email = User.FindFirst(ClaimTypes.Email)?.Value
                        ?? User.FindFirst("preferred_username")?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? User.FindFirst("emails")?.Value
                        ?? User.FindFirst("upn")?.Value;

            var esUsuarioSinFiltro = !string.IsNullOrWhiteSpace(email) && UsuariosSinFiltro.Contains(email);
            vm.PuedeFiltrarPorCliente = esUsuarioSinFiltro;

            var clientes = await GetClientesAsync();
            vm.Clientes = clientes;

            if (esUsuarioSinFiltro)
            {
                try
                {
                    vm.ClienteSeleccionadoId = clienteId;
                    vm.Impresoras = await GetImpresorasAsync(clientes, clienteId);
                    vm.ConsumosMensuales = BuildConsumosMensuales(vm.Impresoras);
                }
                catch (Exception ex)
                {
                    TempData["ImpresorasError"] = $"No se pudieron cargar las impresoras. Detalle: {ex.Message}";
                }

                return View(vm);
            }

            var clienteInfo = await _clienteService.GetClienteByEmailAsync(email ?? string.Empty);
            if (clienteInfo == null || clienteInfo.Id == Guid.Empty)
            {
                TempData["ImpresorasError"] = "No se pudo determinar el cliente del usuario logueado.";
                return View(vm);
            }

            try
            {
                vm.ClienteSeleccionadoId = clienteInfo.Id;
                vm.Impresoras = await GetImpresorasAsync(clientes, clienteInfo.Id);
            }
            catch (Exception ex)
            {
                TempData["ImpresorasError"] = $"No se pudieron cargar las impresoras. Detalle: {ex.Message}";
            }

            return View(vm);
        }

        private static List<ConsumoMensualVm> BuildConsumosMensuales(List<ImpresoraVm> impresoras)
        {
            var resultado = new List<ConsumoMensualVm>();
            if (impresoras == null || impresoras.Count == 0) return resultado;

            foreach (var impresora in impresoras)
            {
                var lecturas = (impresora.Contadores ?? new List<ContadorVm>())
                    .Where(c => c.FechaLectura.HasValue)
                    .Select(c => new
                    {
                        Fecha = c.FechaLectura!.Value,
                        Valor = ParseCounter(c.ContadorPaginas)
                    })
                    .Where(x => x.Valor.HasValue)
                    .OrderBy(x => x.Fecha)
                    .ToList();

                if (lecturas.Count < 2) continue;

                var consumoPorMes = new Dictionary<(int Year, int Month), long>();

                for (var i = 1; i < lecturas.Count; i++)
                {
                    var lecturaActual = lecturas[i];
                    var lecturaAnterior = lecturas[i - 1];
                    var delta = lecturaActual.Valor!.Value - lecturaAnterior.Valor!.Value;
                    if (delta < 0) continue;

                    var llaveMes = (lecturaActual.Fecha.Year, lecturaActual.Fecha.Month);
                    consumoPorMes.TryGetValue(llaveMes, out var acumulado);
                    consumoPorMes[llaveMes] = acumulado + delta;
                }

                resultado.AddRange(consumoPorMes
                    .OrderBy(k => k.Key.Year)
                    .ThenBy(k => k.Key.Month)
                    .Select(k => new ConsumoMensualVm
                    {
                        Serial = impresora.Serial,
                        ClienteNombre = impresora.ClienteNombre,
                        Periodo = new DateTime(k.Key.Year, k.Key.Month, 1),
                        ConsumoPaginas = k.Value
                    }));
            }

            return resultado
                .OrderByDescending(c => c.Periodo)
                .ThenBy(c => c.Serial)
                .ToList();
        }

        private static long? ParseCounter(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return null;

            var digits = new string(valor.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(digits)) return null;

            return long.TryParse(digits, out var parsed) ? parsed : null;
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

        private async Task<List<ImpresoraVm>> GetImpresorasAsync(List<ClienteFiltroVm> clientes, Guid? clienteId = null)
        {
            var query = "cr07a_equipos" +
                        "?$select=cr07a_nombredelequipo,cr07a_categoriadeequipo,cr07a_referencia,cr07a_ultimoniveldetoner,cr07a_fechaultimalectura,cr07a_equipoid,_cr07a_cliente_value" +
                        "&$orderby=cr07a_nombredelequipo";

            if (clienteId.HasValue && clienteId.Value != Guid.Empty)
            {
                var filter = $"_cr07a_cliente_value eq {clienteId:D}";
                query += $"&$filter={filter}";
            }

            using var printersJson = await _dv.GetAsync(query);
            var clientePorId = (clientes ?? new List<ClienteFiltroVm>())
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First().Nombre);

            var impresorasBase = printersJson.RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(e =>
                {
                    var serial = GetString(e, "cr07a_nombredelequipo");
                    var clienteGuid = GetGuid(e, "_cr07a_cliente_value");
                    clientePorId.TryGetValue(clienteGuid, out var clienteNombre);

                    return new ImpresoraVm
                    {
                        Id = GetGuid(e, "cr07a_equipoid"),
                        Serial = serial,
                        Categoria = GetString(e, "cr07a_categoriadeequipo"),
                        ClienteNombre = clienteNombre ?? string.Empty,
                        Referencia = GetString(e, "cr07a_referencia"),
                        UltimoNivelToner = GetString(e, "cr07a_ultimoniveldetoner"),
                        FechaUltimaLectura = GetDateTime(e, "cr07a_fechaultimalectura")
                    };
                })
                .ToList();

            var semaphore = new SemaphoreSlim(6);
            var impresorasTasks = impresorasBase.Select(async impresora =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var serial = impresora.Serial;
                    var contadoresTask = GetContadoresPorSerialAsync(impresora.Id, serial);
                    var mantenimientosTask = GetMantenimientosPorEquipoAsync(impresora.Id);

                    await Task.WhenAll(contadoresTask, mantenimientosTask);
                    impresora.Contadores = contadoresTask.Result;
                    impresora.Mantenimientos = mantenimientosTask.Result;
                }
                finally
                {
                    semaphore.Release();
                }

                return impresora;
            });

            var impresoras = await Task.WhenAll(impresorasTasks);
            return impresoras.ToList();
        }

        private async Task<List<ClienteFiltroVm>> GetClientesAsync()
        {
            const string query = "cr07a_clientes?$select=cr07a_clienteid,cr07a_nombre&$orderby=cr07a_nombre";
            using var clientesJson = await _dv.GetAsync(query);

            return clientesJson.RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(c => new ClienteFiltroVm
                {
                    Id = GetGuid(c, "cr07a_clienteid"),
                    Nombre = GetString(c, "cr07a_nombre")
                })
                .Where(c => c.Id != Guid.Empty)
                .ToList();
        }

        private async Task<List<MantenimientoVm>> GetMantenimientosPorEquipoAsync(Guid equipoId)
        {
            if (equipoId == Guid.Empty) return new List<MantenimientoVm>();

            var filter = $"_cr07a_iddeequipo_value eq {equipoId:D}";
            var query = "cr07a_mantenimientos" +
                             "?$select=cr07a_mantenimiento1,cr07a_fechademantenimiento,cr07a_descripciondelmantenimiento,cr07a_actadeentregadeservicio,cr07a_id,_cr07a_iddeequipo_value" +
                        $"&$filter={filter}" +
                        "&$orderby=cr07a_fechademantenimiento desc";

            using var json = await _dv.GetAsync(query);
            return json.RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(m => new MantenimientoVm
                {
                    Id = GetGuid(m, "cr07a_id"),
                    Titulo = GetString(m, "cr07a_mantenimiento1"),
                    FechaMantenimiento = GetDateTime(m, "cr07a_fechademantenimiento"),
                    Descripcion = GetString(m, "cr07a_descripciondelmantenimiento"),
                    TieneActa = m.TryGetProperty("cr07a_actadeentregadeservicio", out var acta) && acta.ValueKind != JsonValueKind.Null
                })
                .ToList();
        }

 private async Task<List<ContadorVm>> GetContadoresPorSerialAsync(Guid equipoId, string serial)        {
 if (equipoId == Guid.Empty && string.IsNullOrWhiteSpace(serial)) return new List<ContadorVm>();

            var filter = equipoId != Guid.Empty
                ? $"_cr07a_equipo_value eq {equipoId:D}"
                : $"cr07a_equipo eq '{serial.Replace("'", "''")}'";
            var query = "cr07a_contadoresmensualesequipos" +
                        "?$select=cr07a_dt_contadorpaginas,cr07a_dt_fechalectura,cr07a_dt_ipaddress,cr07a_dt_niveltoner,cr07a_dt_paginasescaneadas,cr07a_dt_periodo,_cr07a_equipo_value" +
                        $"&$filter={filter}" +
                        "&$orderby=cr07a_dt_fechalectura desc";

            using var json = await _dv.GetAsync(query);
            return json.RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(c => new ContadorVm
                {
                    Periodo = GetString(c, "cr07a_dt_periodo"),
                    ContadorPaginas = GetString(c, "cr07a_dt_contadorpaginas"),
                    FechaLectura = GetDateTime(c, "cr07a_dt_fechalectura"),
                    IpAddress = GetString(c, "cr07a_dt_ipaddress"),
                    NivelToner = GetString(c, "cr07a_dt_niveltoner"),
                    Escaneos = GetString(c, "cr07a_dt_paginasescaneadas")
                })
                .ToList();
        }

        private static string GetString(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return string.Empty;

            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Number => v.TryGetInt64(out var i)
                    ? i.ToString()
                    : v.TryGetDecimal(out var d)
                        ? d.ToString()
                        : v.GetRawText(),
                JsonValueKind.True => "Sí",
                JsonValueKind.False => "No",
                _ => string.Empty
            };
        }

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
