using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class AssessmentController : Controller
    {
        // GET: /Assessment/SeguridadContinuidad
        [HttpGet]
        public IActionResult SeguridadContinuidad()
        {
            // Página independiente; por ahora no requiere modelo de servidor.
            return View();
        }
    }
}