// SupportController.cs
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using DigitalTechApp.Models;
using DigitalTechApp.Services;

namespace DigitalTechApp.Controllers
{
    public class SupportController : Controller
    {
        private readonly ChatService _chat;
        private readonly SearchService _search;

        public SupportController(ChatService chat, SearchService search)
        {
            _chat = chat;
            _search = search;
        }

        [HttpGet]
        public async Task<IActionResult> SoporteTest()
        {
            var vm = new SoporteModel();
            try
            {
                vm.Suggestions = await _chat.GetSuggestedTopicsAsync(top: 8);
            }
            catch (System.Exception ex)
            {
                vm.Errors.Add(ex.Message);
            }
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> SoporteTest(SoporteModel model)
        {
            var vm = new SoporteModel { UserMessage = model.UserMessage };

            try
            {
                vm.AgentResponse = await _chat.AnswerFromDocsAsync(model.UserMessage ?? string.Empty);

                // Diagnóstico opcional: serializar resultados para verificar evidencia
                var results = await _search.SearchChunksAsync(model.UserMessage ?? "*", top: 6);
                vm.SearchJson = System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (System.Exception ex)
            {
                vm.Errors.Add(ex.Message);
            }

            try
            {
                vm.Suggestions = await _chat.GetSuggestedTopicsAsync(top: 8);
            }
            catch { /* no bloquear por sugerencias */ }

            return View(vm);
        }
    }
}