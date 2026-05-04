using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;
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
            var data = await _permissions.GetUsersForPrincipalAsync(GetCurrentEmail());
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

        [HttpPost("Guardar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Guardar(PermisoEditVm model)
        {
            try
            {
                await _permissions.UpsertUserPermissionAsync(
                    GetCurrentEmail(),
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
                await _permissions.DeleteUserPermissionAsync(GetCurrentEmail(), id);
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
            return User.FindFirst(ClaimTypes.Email)?.Value
                   ?? User.FindFirst("preferred_username")?.Value
                   ?? User.FindFirst("email")?.Value
                   ?? User.FindFirst("emails")?.Value
                   ?? User.FindFirst("upn")?.Value
                   ?? User.Identity?.Name;
        }
    }
}
