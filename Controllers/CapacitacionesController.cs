using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Domain.Dataverse;
using DigitalTechClientPortal.Security;

namespace DigitalTechClientPortal.Controllers
{
    [RequireModule(PortalModuleKeys.Capacitaciones)]
    public class CapacitacionesController : Controller
    {
        private readonly CapacitacionService _capacitacionService;
        private readonly DataverseClienteService _clienteService;

        public CapacitacionesController(CapacitacionService capacitacionService, DataverseClienteService clienteService)
        {
            _capacitacionService = capacitacionService 
                ?? throw new ArgumentNullException(nameof(capacitacionService));
            _clienteService = clienteService 
                ?? throw new ArgumentNullException(nameof(clienteService));
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var email = UserEmailResolver.GetCurrentEmail(User);

            var clienteInfo = string.IsNullOrWhiteSpace(email)
                ? null
                : await _clienteService.GetClienteByEmailAsync(email);

            var capacitaciones = new List<CapacitacionDto>();

            if (clienteInfo != null && clienteInfo.Id != Guid.Empty)
            {
                capacitaciones = _capacitacionService.ObtenerCapacitacionesPorCliente(clienteInfo.Id);
            }

            return View("~/Views/Capacitaciones/Index.cshtml", capacitaciones);
        }

        [HttpGet("Capacitaciones/DescargarCuestionario/{id:guid}")]
        public IActionResult DescargarCuestionario(Guid id)
        {
            var fileBytes = _capacitacionService.DescargarCuestionario(id);
            if (fileBytes == null || fileBytes.Length == 0) return NotFound();

            return File(fileBytes, "application/pdf", "Cuestionario.pdf");
        }
    }
}
