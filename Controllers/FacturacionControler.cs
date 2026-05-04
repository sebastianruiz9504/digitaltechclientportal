using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Infrastructure.Dataverse;
using DigitalTechClientPortal.Domain.Dataverse;
using DigitalTechClientPortal.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Security;

namespace DigitalTechClientPortal.Web.Controllers
{
    [RequireModule(PortalModuleKeys.Facturacion)]
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
            var email = UserEmailResolver.GetCurrentEmail(User);

            // Obtener información del cliente desde Dataverse
            var clienteInfo = string.IsNullOrWhiteSpace(email)
                ? null
                : await _clienteService.GetClienteByEmailAsync(email);

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
