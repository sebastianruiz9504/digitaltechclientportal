using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    [Route("[controller]")]
    public class GraphTestController : Controller
    {
        private readonly GraphClientFactory _graphFactory;

        public GraphTestController(GraphClientFactory graphFactory)
        {
            _graphFactory = graphFactory;
        }

        // GET /GraphTest/Users
        [HttpGet("Users")]
        public async Task<IActionResult> GetUsers()
        {
            var client = await _graphFactory.CreateClientAsync();
            var response = await client.GetAsync("https://graph.microsoft.com/v1.0/users?$top=5");
            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }

        // GET /GraphTest/Alerts
        [HttpGet("Alerts")]
        public async Task<IActionResult> GetAlerts()
        {
            var client = await _graphFactory.CreateClientAsync();
            var response = await client.GetAsync("https://graph.microsoft.com/v1.0/security/alerts?$top=5");
            var json = await response.Content.ReadAsStringAsync();
            return Content(json, "application/json");
        }
    }
}