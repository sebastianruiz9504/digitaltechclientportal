using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    public class CalendarioController : Controller
    {
        private readonly GraphCalendarService _graphSvc;

        public CalendarioController(GraphCalendarService graphSvc)
        {
            _graphSvc = graphSvc;
        }

        [HttpGet]
        public async Task<IActionResult> Disponibilidad()
        {
            var disponibilidad = await _graphSvc.GetDisponibilidadSemanaAsync();
            return View("~/Views/Calendario/Disponibilidad.cshtml", disponibilidad);
        }
        public IActionResult DisponibilidadModal()
{
    return PartialView("Disponibilidad");
}
    }
}