// Controllers/PingController.cs
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    public class PingController : Controller
    {
        [HttpGet("ping")]
        public IActionResult Ping() => Ok("pong");
    }
}