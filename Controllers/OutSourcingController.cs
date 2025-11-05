using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    public class OutsourcingController : Controller
    {
        private readonly SecurityDataService _securityService;

        public OutsourcingController(SecurityDataService securityService)
        {
            _securityService = securityService;
        }

        public async Task<IActionResult> Index()
        {
            var vm = new SecurityDashboardVm
            {
                SecureScore = await _securityService.GetSecureScoreAsync(),
                
            };
            return View(vm);
        }
    }
}