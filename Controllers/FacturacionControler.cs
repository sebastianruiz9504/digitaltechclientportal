using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Infrastructure.Dataverse;
using DigitalTechClientPortal.Domain.Dataverse;
using DigitalTechClientPortal.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Web.Controllers
{
    public class FacturacionController : Controller
    {
        private readonly IDataverseService _dv;
        private readonly DataverseClienteService _clienteService;

        public FacturacionController(IDataverseService dv, DataverseClienteService clienteService)
        {
            _dv = dv ?? throw new ArgumentNullException(nameof(dv));
            _clienteService = clienteService ?? throw new ArgumentNullException(nameof(clienteService));
        }

        public async Task<IActionResult> Index()
        {
            if (User.HasClaim("limited_access", "true"))
    return Forbid(); // o RedirectToAction("Index","Home")

            // Obtener el email del usuario logueado desde los claims
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                        ?? User.FindFirst("preferred_username")?.Value
                        ?? User.FindFirst("email")?.Value
                        ?? User.FindFirst("emails")?.Value
                        ?? User.FindFirst("upn")?.Value;

            // Obtener información del cliente desde Dataverse
            var clienteInfo = await _clienteService.GetClienteByEmailAsync(email);

            var facturas = new List<FacturaDto>();

            if (clienteInfo != null && clienteInfo.Id != Guid.Empty)
            {
                // Filtrar facturas por cliente logueado
                facturas = _dv.GetFacturasPorCliente(clienteInfo.Id).ToList();
            }

            return View(new FacturasViewModel
            {
                Facturas = facturas
            });
        }
    }
}