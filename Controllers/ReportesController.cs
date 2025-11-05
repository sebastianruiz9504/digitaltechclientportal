using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Controllers
{
    public class ReportesController : Controller
    {
        private readonly ReportesCloudService _reportesService;

        public ReportesController(ReportesCloudService reportesService)
        {
            _reportesService = reportesService;
        }

        public async Task<IActionResult> Index()
        {
            var email =
                User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.FindFirst("emails")?.Value
                ?? User.FindFirst("upn")?.Value;

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