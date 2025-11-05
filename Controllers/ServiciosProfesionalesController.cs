using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class ServiciosProfesionalesController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            ViewData["Title"] = "Servicios profesionales";
            return View();
        }
    }
}