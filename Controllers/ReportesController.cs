using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Security;
using System;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Controllers
{
    [RequireModule(PortalModuleKeys.Reportes)]
    public class ReportesController : Controller
    {
        private readonly ReportesCloudService _reportesService;

        public ReportesController(ReportesCloudService reportesService)
        {
            _reportesService = reportesService;
        }

        public async Task<IActionResult> Index()
        {
            var email = UserEmailResolver.GetCurrentEmail(User);

            var reportes = await _reportesService.GetReportesByUserEmailAsync(email ?? string.Empty);
            return View(reportes);
        }

        public IActionResult Descargar(Guid id)
        {
            var fileData = _reportesService.DescargarAdjunto(id);
            if (fileData == null)
                return NotFound();

            return File(fileData.Value.FileBytes, "application/pdf", fileData.Value.FileName);
        }
    }
}
