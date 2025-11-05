using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Controllers
{
    public class AcademyController : Controller
    {
        private readonly YouTubeService _youTubeService;
        private readonly ILogger<AcademyController> _logger;

        public AcademyController(YouTubeService youTubeService, ILogger<AcademyController> logger)
        {
            _youTubeService = youTubeService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Videos()
        {
            try
            {
                var videos = await _youTubeService.GetPlaylistVideosAsync();
                return Json(videos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo videos de YouTube");
                return StatusCode(500, new { error = "Error al obtener videos", detail = ex.Message });
            }
        }
    }
}