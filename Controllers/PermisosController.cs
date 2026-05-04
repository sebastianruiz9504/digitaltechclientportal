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
        private readonly DataverseClienteService _clienteService;

        public PermisosController(
            PortalPermissionService permissions,
            DataverseClienteService clienteService)
        {
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            _clienteService = clienteService ?? throw new ArgumentNullException(nameof(clienteService));
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index()
        {
            var permiso = await ResolvePermissionContextAsync();
            if (!permiso.CanManage)
            {
                return RedirectToAction(nameof(Denegado));
            }

            var data = await _permissions.GetUsersForClienteAsync(permiso.ClienteId, permiso.ClienteNombre);
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
                var permiso = await ResolvePermissionContextAsync();
                if (!permiso.CanManage)
                {
                    TempData["PermisosError"] = "No se pudo resolver el cliente para administrar permisos.";
                    return RedirectToAction(nameof(Index));
                }

                await _permissions.UpsertUserPermissionForClienteAsync(
                    permiso.ClienteId,
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
                var permiso = await ResolvePermissionContextAsync();
                if (!permiso.CanManage)
                {
                    TempData["PermisosError"] = "No se pudo resolver el cliente para administrar permisos.";
                    return RedirectToAction(nameof(Index));
                }

                await _permissions.DeleteUserPermissionForClienteAsync(permiso.ClienteId, id);
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

        private async Task<(bool CanManage, Guid ClienteId, string? ClienteNombre)> ResolvePermissionContextAsync()
        {
            var emails = GetCurrentEmails();
            if (emails.Count == 0)
            {
                return (false, Guid.Empty, null);
            }

            PortalAccessContext access;
            try
            {
                access = await _permissions.GetAccessForEmailsAsync(emails);
            }
            catch
            {
                return (false, Guid.Empty, null);
            }

            var canManage = access.IsPrincipal ||
                (access.IsLimited &&
                 access.IsActive &&
                 PortalModuleKeys.AllKeys.All(moduleKey => access.AllowedModules.Contains(moduleKey)));

            if (!canManage)
            {
                return (false, Guid.Empty, null);
            }

            if (access.ClienteId != Guid.Empty)
            {
                return (true, access.ClienteId, access.ClienteNombre);
            }

            foreach (var email in emails)
            {
                try
                {
                    var cliente = await _clienteService.GetClienteByEmailAsync(email);
                    if (cliente != null && cliente.Id != Guid.Empty)
                    {
                        return (true, cliente.Id, cliente.Nombre);
                    }
                }
                catch
                {
                }
            }

            return (false, Guid.Empty, null);
        }
    }
}
