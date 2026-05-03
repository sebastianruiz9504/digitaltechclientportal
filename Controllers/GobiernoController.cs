using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public sealed class GobiernoController : Controller
    {
        private readonly M365GovernanceDataService _governanceData;
        private readonly M365OptimizationAiService _optimizationAi;
        private readonly ILogger<GobiernoController> _logger;

        public GobiernoController(
            M365GovernanceDataService governanceData,
            M365OptimizationAiService optimizationAi,
            ILogger<GobiernoController> logger)
        {
            _governanceData = governanceData;
            _optimizationAi = optimizationAi;
            _logger = logger;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index([FromQuery] string period = "D90")
        {
            var vm = await LoadGovernanceAsync(period);
            return View(vm);
        }

        [HttpGet("PlanOptimizacion")]
        public async Task<IActionResult> PlanOptimizacion([FromQuery] string period = "D90")
        {
            var vm = await LoadGovernanceAsync(period);

            if (vm.DataSources.Any(s => s.IsAvailable))
            {
                try
                {
                    vm.OptimizationPlanAi = await _optimizationAi.GenerateAsync(vm, HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo generar el plan AI de optimización Microsoft 365.");
                    vm.AiPlanError = BuildAiPlanError(ex);
                }
            }

            return View(vm);
        }

        private async Task<M365GovernanceVm> LoadGovernanceAsync(string period)
        {
            var normalized = NormalizePeriod(period);
            return await _governanceData.CollectAsync(normalized, top: 250, HttpContext.RequestAborted);
        }

        private static string NormalizePeriod(string period)
        {
            return period?.Trim().ToUpperInvariant() switch
            {
                "D7" => "D7",
                "D30" => "D30",
                "D180" => "D180",
                _ => "D90"
            };
        }

        private static string BuildAiPlanError(Exception ex)
        {
            var message = ex.Message;
            if (ex.InnerException != null && !string.Equals(ex.InnerException.Message, message, StringComparison.Ordinal))
            {
                message = $"{message} | Inner: {ex.InnerException.Message}";
            }

            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.Length > 900)
            {
                message = message[..900] + "...";
            }

            return $"No se pudo generar el plan de optimización con AI. Detalle técnico: {message}";
        }
    }
}
