using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public class SummaryService
    {
        private readonly DataverseSoporteService _dv;
        private readonly DataverseClienteService _clienteService;
        private readonly CapacitacionService _capacitacionService;
        private readonly ReportesCloudService _reportesCloudService;

        public SummaryService(
            DataverseSoporteService dv,
            DataverseClienteService clienteService,
            CapacitacionService capacitacionService,
            ReportesCloudService reportesCloudService)
        {
            _dv = dv ?? throw new ArgumentNullException(nameof(dv));
            _clienteService = clienteService ?? throw new ArgumentNullException(nameof(clienteService));
            _capacitacionService = capacitacionService ?? throw new ArgumentNullException(nameof(capacitacionService));
            _reportesCloudService = reportesCloudService ?? throw new ArgumentNullException(nameof(reportesCloudService));
        }

        public async Task<ResumenDto> GetResumenAsync(string? userEmail, RangoResumen rango)
        {
            var clienteInfo = await _clienteService.GetClienteByEmailAsync(userEmail ?? string.Empty);
            if (clienteInfo == null || clienteInfo.Id == Guid.Empty)
                return new ResumenDto();

            var clienteId = clienteInfo.Id;

            var (ticketDateFilter, mantenimientoDateFilter, _, _) = BuildDateFilters(rango);

            // 1) Tickets Cloud: conteo + suma horas (base)
            var ticketsCount = 0;
            decimal horasSumTickets = 0m;
            try
            {
                var ticketsQuery = new StringBuilder("cr07a_tickets");
                ticketsQuery.Append("?$select=cr07a_fechacreacion,cr07a_horas");
                ticketsQuery.Append($"&$filter=_cr07a_cliente_value eq {clienteId}");
                if (!string.IsNullOrEmpty(ticketDateFilter))
                    ticketsQuery.Append($" and {ticketDateFilter}");

                ticketsQuery.Append("&$orderby=cr07a_fechacreacion desc");

                using var ticketsJson = await _dv.GetAsync(ticketsQuery.ToString());
                var values = ticketsJson.RootElement.GetProperty("value");

                ticketsCount = values.GetArrayLength();

                foreach (var e in values.EnumerateArray())
                {
                    if (e.TryGetProperty("cr07a_horas", out var horasProp))
                    {
                        if (horasProp.ValueKind == JsonValueKind.Number && horasProp.TryGetDecimal(out var d))
                            horasSumTickets += d;
                        else if (horasProp.ValueKind == JsonValueKind.String &&
                                 decimal.TryParse(horasProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                            horasSumTickets += ds;
                    }
                }
            }
            catch
            {
                // Resiliente
            }

            // 2) Mantenimientos Copiers: conteo
            var copiersCount = 0;
            try
            {
                var mantQuery = new StringBuilder("cr07a_mantenimientos");
                mantQuery.Append("?$select=cr07a_fechademantenimiento");
                mantQuery.Append($"&$filter=_cr07a_cliente_value eq {clienteId}");
                if (!string.IsNullOrEmpty(mantenimientoDateFilter))
                    mantQuery.Append($" and {mantenimientoDateFilter}");

                mantQuery.Append("&$orderby=cr07a_fechademantenimiento desc");

                using var mantJson = await _dv.GetAsync(mantQuery.ToString());
                copiersCount = mantJson.RootElement.GetProperty("value").GetArrayLength();
            }
            catch
            {
            }

            // 3) Capacitaciones: conteo (filtrado por fecha en memoria)
            var capacitacionesCount = 0;
            try
            {
                var caps = _capacitacionService.ObtenerCapacitacionesPorCliente(clienteId);
                capacitacionesCount = CountByDateRange(caps, c => c.Fecha, rango);
            }
            catch
            {
            }

            // 4) Reportes Cloud: conteo (filtrado por fecha en memoria)
            var reportesCount = 0;
            try
            {
                var email = userEmail ?? string.Empty;
                var reportes = await _reportesCloudService.GetReportesByUserEmailAsync(email);
                reportesCount = CountByDateRange(reportes, r => r.Fecha, rango);
            }
            catch
            {
            }

            // 5) Horas entregadas = horas tickets + 2h por cada capacitación + 2h por cada reporte + 2h por cada mantenimiento
            var horasExtra = (capacitacionesCount + reportesCount + copiersCount) * 2m;
            var horasEntregadas = Math.Round(horasSumTickets + horasExtra, 2);

            return new ResumenDto
            {
                TicketsCloud = ticketsCount,
                TicketsCopiers = copiersCount,
                Capacitaciones = capacitacionesCount,
                HorasEntregadas = horasEntregadas,
                Reportes = reportesCount
            };
        }

        private static (string ticketDateFilter, string mantenimientoDateFilter, string capacitacionDateFilter, string reporteDateFilter)
            BuildDateFilters(RangoResumen rango)
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime start, end;

            switch (rango)
            {
                case RangoResumen.Mes:
                    start = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    end = start.AddMonths(1);
                    break;
                case RangoResumen.Anio:
                    start = new DateTime(utcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    end = start.AddYears(1);
                    break;
                case RangoResumen.Total:
                default:
                    return (string.Empty, string.Empty, string.Empty, string.Empty);
            }

            string isoStart = start.ToString("o");
            string isoEnd = end.ToString("o");

            string ticketFilter = $"cr07a_fechacreacion ge {isoStart} and cr07a_fechacreacion lt {isoEnd}";
            string mantFilter = $"cr07a_fechademantenimiento ge {isoStart} and cr07a_fechademantenimiento lt {isoEnd}";
            string capFilter = $"cr07a_fecha ge {isoStart} and cr07a_fecha lt {isoEnd}";
            string repFilter = $"cr07a_fecha ge {isoStart} and cr07a_fecha lt {isoEnd}";

            return (ticketFilter, mantFilter, capFilter, repFilter);
        }

        private static int CountByDateRange<T>(System.Collections.Generic.IEnumerable<T> items, Func<T, DateTime> dateSelector, RangoResumen rango)
        {
            if (items == null) return 0;
            DateTime now = DateTime.UtcNow;

            switch (rango)
            {
                case RangoResumen.Mes:
                    int m = now.Month; int y = now.Year;
                    return System.Linq.Enumerable.Count(items, x =>
                    {
                        var d = dateSelector(x);
                        return d.Year == y && d.Month == m;
                    });

                case RangoResumen.Anio:
                    int y2 = now.Year;
                    return System.Linq.Enumerable.Count(items, x => dateSelector(x).Year == y2);

                case RangoResumen.Total:
                default:
                    return System.Linq.Enumerable.Count(items);
            }
        }
    }
}