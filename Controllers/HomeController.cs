using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Security;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly DataverseHomeService _homeService;
        private readonly DataverseClienteService _clienteService;
        private readonly SummaryService _summaryService;

        public HomeController(
            ILogger<HomeController> logger,
            DataverseHomeService homeService,
            DataverseClienteService clienteService,
            SummaryService summaryService)
        {
            _logger = logger;
            _homeService = homeService;
            _clienteService = clienteService;
            _summaryService = summaryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? range = null)
        {
            var candidateEmails = UserEmailResolver.GetCandidateEmails(User);
            var email = candidateEmails.FirstOrDefault();

            string? clienteNombre = null;
            DataverseClienteService.ClienteInfo? clienteInfo = null;
            try
            {
                foreach (var candidateEmail in candidateEmails)
                {
                    clienteInfo = await _clienteService.GetClienteByEmailAsync(candidateEmail);
                    if (clienteInfo != null)
                    {
                        email = candidateEmail;
                        clienteNombre = clienteInfo.Nombre;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo cliente desde Dataverse");
            }

            ViewBag.CompanyName = clienteNombre ?? "(Cliente desconocido)";
            ViewBag.WorkspaceName = email ?? "(Email no disponible)";

            var cloudProducts = new List<ProductVm>();
            try
            {
                if (clienteInfo != null && clienteInfo.Id != Guid.Empty)
                {
                    cloudProducts = await _homeService.GetSalesPerformanceProductsAsync(clienteInfo.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar productos de Sales Performance Record (Cloud)");
                TempData["HomeError"] = "No se pudieron cargar los productos de Cloud.";
            }

            var copiersProducts = new List<ProductVm>();
            try
            {
                if (clienteInfo != null && clienteInfo.Id != Guid.Empty)
                {
                    copiersProducts = await _homeService.GetCopiersProductsAsync(clienteInfo.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar productos Copiers desde cr07a_productoscopiers");
                TempData["CopiersError"] = "No se pudieron cargar los productos de Copiers.";
            }

            ViewBag.Copiers = copiersProducts;

            var rango = ParseRange(range);
            ResumenDto resumen = new ResumenDto();
            try
            {
                resumen = await _summaryService.GetResumenAsync(email, rango);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar resumen consolidado para Home");
                TempData["SummaryError"] = "No se pudo cargar el resumen.";
            }

            ViewBag.Summary = resumen;
            ViewBag.SelectedRange = rango;

            return View(cloudProducts);
        }

        private static RangoResumen ParseRange(string? range)
        {
            if (string.IsNullOrWhiteSpace(range)) return RangoResumen.Mes;
            range = range.Trim().ToLowerInvariant();

            return range switch
            {
                "mes" => RangoResumen.Mes,
                "anio" => RangoResumen.Anio,
                "año" => RangoResumen.Anio,
                "total" => RangoResumen.Total,
                _ => RangoResumen.Mes
            };
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
