using System;
using System.Linq;
using System.Threading.Tasks;
using DigitalTechClientPortal.Security;
using DigitalTechClientPortal.Services;
using DigitalTechClientPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Web.Controllers
{
    [RequireModule(PortalModuleKeys.Licenciamiento)]
    public class LicenciamientoController : Controller
    {
        private readonly LicenciamientoService _licenciamientoService;

        public LicenciamientoController(LicenciamientoService licenciamientoService)
        {
            _licenciamientoService = licenciamientoService ?? throw new ArgumentNullException(nameof(licenciamientoService));
        }

        [HttpGet]
        public async Task<IActionResult> Index(Guid? clienteId = null, int? mes = null, int? anio = null)
        {
            var vm = await _licenciamientoService.BuildViewModelAsync(
                UserEmailResolver.GetCandidateEmails(User),
                clienteId,
                mes,
                anio,
                TempData["LicenciamientoMensaje"] as string,
                TempData["LicenciamientoError"] as string);

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSubRazon(CrearSubRazonLicenciamientoVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.CrearSubRazonAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Subrazón social creada en Dataverse.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAsignacion(GuardarAsignacionLicenciamientoVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.GuardarAsignacionAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Asignación de licencias actualizada.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarLicencias(SolicitarLicenciasVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var solicitante = UserEmailResolver.GetCurrentEmail(User) ?? string.Empty;
                var clienteId = await _licenciamientoService.SolicitarLicenciasAsync(
                    UserEmailResolver.GetCandidateEmails(User),
                    input,
                    solicitante);

                TempData["LicenciamientoMensaje"] = "Solicitud de aprovisionamiento creada en Dataverse.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearSolicitudCliente(CrearSolicitudClienteVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var solicitante = UserEmailResolver.GetCurrentEmail(User) ?? string.Empty;
                var clienteId = await _licenciamientoService.CrearSolicitudClienteAsync(
                    UserEmailResolver.GetCandidateEmails(User),
                    input,
                    solicitante);

                TempData["LicenciamientoMensaje"] = "Solicitud cargada en solicitudesclientes con estado Pendiente.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarLicenciasSinAsignar(AsignarLicenciasSinAsignarVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var solicitante = UserEmailResolver.GetCurrentEmail(User) ?? string.Empty;
                var clienteId = await _licenciamientoService.AsignarLicenciasSinAsignarAsync(
                    UserEmailResolver.GetCandidateEmails(User),
                    input,
                    solicitante);

                TempData["LicenciamientoMensaje"] = "Licencias sin asignar aplicadas al cliente hijo.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarFechaCorte(ActualizarFechaCorteLicenciamientoVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.ActualizarFechaCorteAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Día de facturación actualizado en productos cloud.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoverLicencias(MoverLicenciasVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.MoverLicenciasAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Licencias movidas entre clientes hijos sin cambiar el total del grupo.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActualizarGrupoEmpresarial(ActualizarGrupoEmpresarialVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.ActualizarGrupoEmpresarialAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Grupo empresarial actualizado.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AsignarAccountIdGrupo(AsignarAccountIdGrupoVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.AsignarAccountIdAGrupoAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Account ID asignado al grupo empresarial.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuitarAccountIdGrupo(QuitarAccountIdGrupoVm input)
        {
            if (!ModelState.IsValid)
            {
                TempData["LicenciamientoError"] = FirstModelError();
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }

            try
            {
                var clienteId = await _licenciamientoService.QuitarAccountIdDelGrupoAsync(UserEmailResolver.GetCandidateEmails(User), input);
                TempData["LicenciamientoMensaje"] = "Account ID retirado del grupo empresarial.";
                return RedirectToIndex(clienteId, input.Mes, input.Anio);
            }
            catch (Exception ex)
            {
                TempData["LicenciamientoError"] = ex.Message;
                return RedirectToIndex(input.ClienteId, input.Mes, input.Anio);
            }
        }

        private RedirectToActionResult RedirectToIndex(Guid clienteId, int mes, int anio)
        {
            return RedirectToAction(nameof(Index), new { clienteId, mes, anio });
        }

        private string FirstModelError()
        {
            return ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                ?? "Revisa los datos enviados.";
        }
    }
}
