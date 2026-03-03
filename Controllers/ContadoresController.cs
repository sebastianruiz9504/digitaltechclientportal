using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class ContadoresController : Controller
    {
        private static readonly HashSet<string> UsuariosAutorizados = new(StringComparer.OrdinalIgnoreCase)
        {
            "jromero@digitaltechcolombia.com",
            "germanruiz@digitaltechcolombia.com",
            "sruiz@digitaltechcolombia.com"
        };

        private readonly DataverseSoporteService _dv;

        public ContadoresController(DataverseSoporteService dv)
        {
            _dv = dv ?? throw new ArgumentNullException(nameof(dv));
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? mes = null, int? anio = null, Guid? clienteId = null)
        {
            var email = GetCurrentEmail();
            if (string.IsNullOrWhiteSpace(email) || !UsuariosAutorizados.Contains(email))
            {
                return Forbid();
            }

            var now = DateTime.Today;
            var mesSeleccionado = mes.GetValueOrDefault(now.Month);
            var anioSeleccionado = anio.GetValueOrDefault(now.Year);

            if (mesSeleccionado is < 1 or > 12)
            {
                mesSeleccionado = now.Month;
            }

            if (anioSeleccionado is < 2000 or > 2100)
            {
                anioSeleccionado = now.Year;
            }

            var vm = new ContadoresIndexVm
            {
                MesSeleccionado = mesSeleccionado,
                AnioSeleccionado = anioSeleccionado,
                ClienteSeleccionadoId = clienteId
            };

            try
            {
                vm.Clientes = await GetClientesAsync();
                vm.Equipos = await GetConsumoPorEquipoAsync(vm.Clientes, vm.PeriodoSeleccionado, clienteId);
                vm.ConsumoPorCliente = vm.Equipos
                    .GroupBy(e => new { e.ClienteId, e.ClienteNombre })
                    .Select(g => new ConsumoClienteVm
                    {
                        ClienteId = g.Key.ClienteId,
                        ClienteNombre = g.Key.ClienteNombre,
                        TotalCopias = g.Sum(x => x.ConsumoCopias ?? 0),
                        TotalEscaneos = g.Sum(x => x.ConsumoEscaneos ?? 0),
                        EquiposConConsumo = g.Count(x => (x.ConsumoCopias ?? 0) > 0 || (x.ConsumoEscaneos ?? 0) > 0)
                    })
                    .OrderBy(x => x.ClienteNombre)
                    .ToList();
            }
            catch (Exception ex)
            {
                vm.Mensaje = $"No se pudo cargar el consumo de contadores. Detalle: {ex.Message}";
            }

            return View(vm);
        }

        private string? GetCurrentEmail()
        {
            return User.FindFirst(ClaimTypes.Email)?.Value
                   ?? User.FindFirst("preferred_username")?.Value
                   ?? User.FindFirst("email")?.Value
                   ?? User.FindFirst("emails")?.Value
                   ?? User.FindFirst("upn")?.Value;
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

        private async Task<List<ConsumoEquipoVm>> GetConsumoPorEquipoAsync(List<ClienteFiltroVm> clientes, DateTime periodo, Guid? clienteId)
        {
            var inicioMes = new DateTime(periodo.Year, periodo.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var inicioMesSiguiente = inicioMes.AddMonths(1);
            var inicioMesAnterior = inicioMes.AddMonths(-1);

            var equipos = await GetEquiposAsync(clientes, clienteId);
            if (equipos.Count == 0) return new List<ConsumoEquipoVm>();

            var semaphore = new SemaphoreSlim(8);
            var tasks = equipos.Select(async equipo =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var actual = await GetUltimaLecturaPorMesAsync(equipo.EquipoId, inicioMes, inicioMesSiguiente);
                    var anterior = await GetUltimaLecturaPorMesAsync(equipo.EquipoId, inicioMesAnterior, inicioMes);

                    var consumoCopias = CalculateDelta(actual.ContadorCopias, anterior.ContadorCopias);
                    var consumoEscaneos = CalculateDelta(actual.ContadorEscaneos, anterior.ContadorEscaneos);

                    int? dias = null;
                    if (actual.Fecha.HasValue && anterior.Fecha.HasValue)
                    {
                        dias = Math.Abs((actual.Fecha.Value.Date - anterior.Fecha.Value.Date).Days);
                    }

                    return new ConsumoEquipoVm
                    {
                        EquipoId = equipo.EquipoId,
                        EquipoNombre = equipo.EquipoNombre,
                        ClienteId = equipo.ClienteId,
                        ClienteNombre = equipo.ClienteNombre,
                        FechaActual = actual.Fecha,
                        FechaAnterior = anterior.Fecha,
                        ContadorActualCopias = actual.ContadorCopias,
                        ContadorActualEscaneos = actual.ContadorEscaneos,
                        ConsumoCopias = consumoCopias,
                        ConsumoEscaneos = consumoEscaneos,
                        DiasEntreTomas = dias
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var data = await Task.WhenAll(tasks);
            return data
                .OrderBy(x => x.ClienteNombre)
                .ThenBy(x => x.EquipoNombre)
                .ToList();
        }

        private static long? CalculateDelta(long? actual, long? anterior)
        {
            if (!actual.HasValue || !anterior.HasValue) return null;
            var delta = actual.Value - anterior.Value;
            return delta < 0 ? null : delta;
        }

        private async Task<List<(Guid EquipoId, string EquipoNombre, Guid ClienteId, string ClienteNombre)>> GetEquiposAsync(List<ClienteFiltroVm> clientes, Guid? clienteId)
        {
            var query = "cr07a_equipos?$select=cr07a_equipoid,cr07a_nombredelequipo,_cr07a_cliente_value&$orderby=cr07a_nombredelequipo";
            if (clienteId.HasValue && clienteId.Value != Guid.Empty)
            {
                query += $"&$filter=_cr07a_cliente_value eq {clienteId.Value:D}";
            }

            var clientePorId = clientes
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First().Nombre);

            using var json = await _dv.GetAsync(query);
            return json.RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(e =>
                {
                    var cliId = GetGuid(e, "_cr07a_cliente_value");
                    clientePorId.TryGetValue(cliId, out var cliNombre);
                    return (
                        EquipoId: GetGuid(e, "cr07a_equipoid"),
                        EquipoNombre: GetString(e, "cr07a_nombredelequipo"),
                        ClienteId: cliId,
                        ClienteNombre: cliNombre ?? string.Empty
                    );
                })
                .Where(x => x.EquipoId != Guid.Empty)
                .ToList();
        }

        private async Task<(DateTime? Fecha, long? ContadorCopias, long? ContadorEscaneos)> GetUltimaLecturaPorMesAsync(Guid equipoId, DateTime inicio, DateTime fin)
        {
            if (equipoId == Guid.Empty)
            {
                return (null, null, null);
            }

            var inicioTxt = inicio.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var finTxt = fin.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            var filter = $"_cr07a_maquina_value eq {equipoId:D} and cr07a_fechadetomadecontador ge {inicioTxt} and cr07a_fechadetomadecontador lt {finTxt}";
            var query = "cr07a_contadores" +
                        "?$select=cr07a_contador,cr07a_contadorescaner,cr07a_fechadetomadecontador,_cr07a_maquina_value" +
                        $"&$filter={filter}" +
                        "&$orderby=cr07a_fechadetomadecontador desc" +
                        "&$top=1";

            using var json = await _dv.GetAsync(query);
            var first = json.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Undefined)
            {
                return (null, null, null);
            }

            return (
                Fecha: GetDateTime(first, "cr07a_fechadetomadecontador"),
                ContadorCopias: GetLong(first, "cr07a_contador"),
                ContadorEscaneos: GetLong(first, "cr07a_contadorescaner")
            );
        }

        private static string GetString(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return string.Empty;

            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Number => v.TryGetInt64(out var i) ? i.ToString(CultureInfo.InvariantCulture) : v.GetRawText(),
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

        private static long? GetLong(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return null;

            if (v.ValueKind == JsonValueKind.Number)
            {
                if (v.TryGetInt64(out var val)) return val;
                if (v.TryGetDecimal(out var dec)) return (long)dec;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                var raw = v.GetString();
                if (string.IsNullOrWhiteSpace(raw)) return null;

                var digits = new string(raw.Where(char.IsDigit).ToArray());
                if (string.IsNullOrWhiteSpace(digits)) return null;

                if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }
}
