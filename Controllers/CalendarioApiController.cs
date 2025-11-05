using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalendarioApiController : ControllerBase
    {
        private readonly GraphCalendarService _graphSvc;

        public CalendarioApiController(GraphCalendarService graphSvc)
        {
            _graphSvc = graphSvc;
        }

        [HttpGet("disponibilidad")]
        public async Task<IActionResult> GetDisponibilidad()
        {
            try
            {
                var disponibilidad = await _graphSvc.GetDisponibilidadSemanaAsync();
                return Ok(disponibilidad);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, "Error consultando la disponibilidad de calendario");
            }
        }

        [HttpPost("reservar")]
        public async Task<IActionResult> Reservar([FromBody] ReservaRequest request)
        {
            if (request == null || request.HoraInicio == default || string.IsNullOrWhiteSpace(request.Tema))
                return BadRequest("Datos inválidos");

            var solicitante = User.FindFirst("preferred_username")?.Value
                           ?? User.FindFirst(ClaimTypes.Email)?.Value
                           ?? User.FindFirst(ClaimTypes.Upn)?.Value;

            if (string.IsNullOrWhiteSpace(solicitante))
                return Unauthorized("No se pudo determinar el correo del usuario autenticado");

            try
            {
                await _graphSvc.CrearReservaAsync(solicitante, request.HoraInicio, request.Tema, request.Observaciones);
                return Ok(new { mensaje = "Reserva creada correctamente" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, "Error creando la reserva");
            }
        }
    }

    public class ReservaRequest
    {
        public DateTime HoraInicio { get; set; }
        public string Tema { get; set; }
        public string Observaciones { get; set; }
    }
}