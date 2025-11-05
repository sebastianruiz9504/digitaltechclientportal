using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public sealed class GraphCalendarService
    {
        private readonly GraphServiceClient _graph;
        private readonly ILogger<GraphCalendarService> _logger;
        private readonly string _instructorUpn;
        private readonly string _timeZone;

        public GraphCalendarService(IConfiguration config, ILogger<GraphCalendarService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var clientId     = config["Graph:ClientId"];
            var tenantId     = config["Graph:TenantId"];
            var clientSecret = config["Graph:ClientSecret"];

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("Faltan credenciales de Graph en configuración.");

            _instructorUpn = config["Instructor:Upn"]
                ?? throw new InvalidOperationException("Falta Instructor:Upn en configuración.");

            _timeZone = string.IsNullOrWhiteSpace(config["Instructor:TimeZone"])
                ? "America/Bogota"
                : config["Instructor:TimeZone"]!;

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _graph = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        }

        public async Task<List<DisponibilidadDto>> GetDisponibilidadSemanaAsync()
        {
            var disponibilidad = new List<DisponibilidadDto>();
            var fechaActual = DateTime.Today;
            var fechaFinal = fechaActual.AddDays(15);

            while (fechaActual <= fechaFinal)
            {
                if (fechaActual.DayOfWeek != DayOfWeek.Saturday &&
                    fechaActual.DayOfWeek != DayOfWeek.Sunday)
                {
                    var start = fechaActual.AddHours(8);
                    var end   = fechaActual.AddHours(16);

                    var resp = await _graph.Users[_instructorUpn].CalendarView.GetAsync(req =>
                    {
                        req.QueryParameters.StartDateTime = start.ToString("o");
                        req.QueryParameters.EndDateTime   = end.ToString("o");
                        req.Headers.Add("Prefer", $"outlook.timezone=\"{_timeZone}\"");
                    });

                    var ocupados = new List<(DateTime Inicio, DateTime Fin)>();

                    if (resp?.Value != null)
                    {
                        foreach (var e in resp.Value)
                        {
                            if (DateTime.TryParse(e.Start?.DateTime, out var inicio) &&
                                DateTime.TryParse(e.End?.DateTime, out var fin))
                            {
                                ocupados.Add((inicio, fin));
                            }
                        }
                    }

                    for (int h = 8; h < 16; h++)
                    {
                        var slotInicio = fechaActual.AddHours(h);
                        var slotFin    = slotInicio.AddHours(1);
                        var estaOcupado = ocupados.Any(o => o.Inicio < slotFin && o.Fin > slotInicio);

                        disponibilidad.Add(new DisponibilidadDto
                        {
                            HoraInicio = slotInicio,
                            HoraFin    = slotFin,
                            Disponible = !estaOcupado
                        });
                    }
                }

                fechaActual = fechaActual.AddDays(1);
            }

            return disponibilidad;
        }

        public async Task CrearReservaAsync(string solicitanteUpn, DateTime horaInicio, string tema, string observaciones)
        {
            var horaFin = horaInicio.AddHours(1);

            var evento = new Event
            {
                Subject = $"{tema} — Reservado por {solicitanteUpn}",
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = string.IsNullOrWhiteSpace(observaciones) ? "" : $"Observaciones: {observaciones}"
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = horaInicio.ToString("o"),
                    TimeZone = _timeZone
                },
                End = new DateTimeTimeZone
                {
                    DateTime = horaFin.ToString("o"),
                    TimeZone = _timeZone
                },
                Location = new Location { DisplayName = "Virtual" },
                Attendees = new List<Attendee>
                {
                    new Attendee
                    {
                        EmailAddress = new EmailAddress { Address = solicitanteUpn },
                        Type = AttendeeType.Required
                    }
                }
            };

            await _graph.Users[_instructorUpn].Events.PostAsync(evento);
        }
    }
}