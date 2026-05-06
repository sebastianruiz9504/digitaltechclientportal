using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [RequireModule(PortalModuleKeys.Gobierno)]
    [Route("[controller]")]
    public sealed class GobiernoController : Controller
    {
        private readonly M365GovernanceDataService _governanceData;
        private readonly M365OptimizationAiService _optimizationAi;
        private readonly GraphPermissionService _graphPermissions;
        private readonly ILogger<GobiernoController> _logger;

        public GobiernoController(
            M365GovernanceDataService governanceData,
            M365OptimizationAiService optimizationAi,
            GraphPermissionService graphPermissions,
            ILogger<GobiernoController> logger)
        {
            _governanceData = governanceData;
            _optimizationAi = optimizationAi;
            _graphPermissions = graphPermissions;
            _logger = logger;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index([FromQuery] string period = "D90")
        {
            var permissionResult = await EnsureGovernancePermissionsAsync(period, "Index");
            if (permissionResult != null)
                return permissionResult;

            var vm = await LoadGovernanceAsync(period);
            return View(vm);
        }

        [HttpGet("PlanOptimizacion")]
        public async Task<IActionResult> PlanOptimizacion([FromQuery] string period = "D90")
        {
            var permissionResult = await EnsureGovernancePermissionsAsync(period, "PlanOptimizacion");
            if (permissionResult != null)
                return permissionResult;

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

        private async Task<IActionResult?> EnsureGovernancePermissionsAsync(string period, string viewName)
        {
            var permissionStatus = await _graphPermissions.GetGovernancePermissionStatusAsync();
            if (!permissionStatus.HasMissingRequiredScopes)
                return null;

            if (!Request.Query.ContainsKey("consentChecked"))
                return RedirectToAction("Consent", "Login", new { returnUrl = BuildConsentReturnUrl() });

            var vm = new M365GovernanceVm
            {
                Period = NormalizePeriod(period),
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                PermissionStatus = permissionStatus,
                GraphError = BuildMissingPermissionsMessage(permissionStatus)
            };

            vm.DataSources.Add(new SecurityDataSourceStatus
            {
                Name = "Microsoft Graph",
                IsAvailable = false,
                Count = 0,
                Message = vm.GraphError
            });

            return View(viewName, vm);
        }

        private async Task<M365GovernanceVm> LoadGovernanceAsync(string period)
        {
            var normalized = NormalizePeriod(period);
            var vm = await _governanceData.CollectAsync(normalized, top: 250, HttpContext.RequestAborted);
            vm.PermissionStatus = await _graphPermissions.GetGovernancePermissionStatusAsync();
            return vm;
        }

        private string BuildConsentReturnUrl()
        {
            var path = $"{Request.PathBase}{Request.Path}";
            var query = Request.QueryString.HasValue ? Request.QueryString.Value! : "";

            if (Request.Query.ContainsKey("consentChecked"))
                return path + query;

            var separator = string.IsNullOrWhiteSpace(query) ? "?" : "&";
            return $"{path}{query}{separator}consentChecked=1";
        }

        private static string BuildMissingPermissionsMessage(SecurityPermissionStatus permissionStatus)
        {
            return $"El token actual solo tiene {permissionStatus.GrantedRequiredCount} de {permissionStatus.RequiredCount} permisos requeridos para leer el modulo de Gobierno M365. Se necesita consentimiento del tenant para continuar.";
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
