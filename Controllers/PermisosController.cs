using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Security;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public sealed class PermisosController : Controller
    {
        private readonly PortalPermissionService _permissions;

        public PermisosController(PortalPermissionService permissions)
        {
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var data = await _permissions.GetUsersForPrincipalAsync(GetCurrentEmails());
            if (!data.IsPrincipal)
            {
                return Forbid();
            }

            var vm = new PermisosIndexVm
            {
                ClienteId = data.ClienteId,
                ClienteNombre = data.ClienteNombre ?? string.Empty,
                PermissionColumnsAvailable = data.PermissionColumnsAvailable,
                Modulos = PortalModuleKeys.All,
                Usuarios = data.Users.Select(u => new UsuarioPermisoVm
                {
                    Id = u.Id,
                    Email = u.Email,
                    Nombre = u.DisplayName,
                    Activo = u.Active,
                    ModulosSeleccionados = u.Modules.ToList()
                }).ToList(),
                Nuevo = new PermisoEditVm
                {
                    Activo = true
                }
            };

            return View(vm);
        }

        [AllowAnonymous]
        [HttpGet("Denegado")]
        public IActionResult Denegado([FromQuery] string? modulo = null)
        {
            var module = PortalModuleKeys.All.FirstOrDefault(m =>
                string.Equals(m.Key, modulo, StringComparison.OrdinalIgnoreCase));

            ViewBag.Modulo = module?.Label;
            ViewBag.Email = GetCurrentEmail();
            ViewBag.CandidateEmails = GetCurrentEmails();
            return View();
        }

        [HttpPost("Guardar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(PermisoEditVm model)
        {
            try
            {
                await _permissions.UpsertUserPermissionAsync(
                    GetCurrentEmails(),
                    new PermissionUserRecord
                    {
                        Id = model.Id.GetValueOrDefault(),
                        Email = model.Email,
                        DisplayName = model.Nombre,
                        Active = model.Activo,
                        Modules = model.ModulosSeleccionados ?? new()
                    });

                TempData["PermisosOk"] = "Permisos guardados.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                TempData["PermisosError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Eliminar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Eliminar(Guid id)
        {
            try
            {
                await _permissions.DeleteUserPermissionAsync(GetCurrentEmails(), id);
                TempData["PermisosOk"] = "Usuario eliminado.";
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
            {
                TempData["PermisosError"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private string? GetCurrentEmail()
        {
            return UserEmailResolver.GetCurrentEmail(User);
        }

        private IReadOnlyList<string> GetCurrentEmails()
        {
            return UserEmailResolver.GetCandidateEmails(User);
        }
    }
}
