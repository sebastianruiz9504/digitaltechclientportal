using DigitalTechClientPortal.Web.Models;
using DigitalTechClientPortal.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Web.ViewComponents
{
    public sealed class RightContactsViewComponent : ViewComponent
    {
        private readonly ContactsPanelService _svc;

        public RightContactsViewComponent(ContactsPanelService svc)
        {
            _svc = svc;
        }

        // Permite pasar clienteId opcionalmente desde la vista
        public async Task<IViewComponentResult> InvokeAsync(Guid? clienteId = null)
        {
            var data = await _svc.GetRightContactsAsync(clienteId);
            return View(data);
        }
    }
}