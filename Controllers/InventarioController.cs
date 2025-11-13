using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Controllers
{
    // DTO simple para reportes (POCO con setters públicos — evita reflexión)
    public sealed class ReportItem
    {
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [Authorize]
    [Route("[controller]")]
    public class InventarioController : Controller
    {
        private readonly GraphClientFactory _graphFactory;
        private readonly ServiceClient _dataverse;
        private readonly ClientesService _clientesService;
        private readonly DataverseSoporteService _dataverseFiles;

        private const int MaxUsuariosSinLicenciaListado = 100;
        private const string EquiposEntityName = "cr07a_equiposdigitalapp";
        private const string EquiposEntitySetName = "cr07a_equiposdigitalapps";
        private const string ActaColumnName = "cr07a_actadeentrega";
        private const string PropioORentaAttribute = "cr07a_propioorenta";

        private sealed record UnlicensedUserSummary(string? DisplayName, string? UserPrincipalName, string? Mail, string? Department);

        public InventarioController(GraphClientFactory graphFactory, ServiceClient dataverse, ClientesService clientesService, DataverseSoporteService dataverseFiles)
        {
            _graphFactory = graphFactory;
            _dataverse = dataverse;
            _clientesService = clientesService;
            _dataverseFiles = dataverseFiles;
        }

        // Usuarios (vista independiente)
        [HttpGet("Usuarios")]
        public async Task<IActionResult> Usuarios([FromQuery] int pageSize = 20, [FromQuery] string? skiptoken = null, [FromQuery] string? term = null)
        {
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch (InvalidOperationException ex) { return Redirect("/Login/Index?reason=" + Uri.EscapeDataString(ex.Message)); }

            string url;
            if (string.IsNullOrWhiteSpace(term))
            {
                url = "https://graph.microsoft.com/v1.0/users" +
                    "?$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone" +
                    $"&$top={pageSize}";
            }
            else
            {
                var safeTerm = term.Replace("'", "''");
                url = "https://graph.microsoft.com/v1.0/users" +
                    $"?$filter=startswith(displayName,'{safeTerm}')" +
                    "&$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone" +
                    $"&$top={pageSize}";
            }

            if (!string.IsNullOrEmpty(skiptoken))
            {
                url += $"&$skiptoken={Uri.EscapeDataString(skiptoken)}";
            }

            using var response = await client.GetAsync(url);
            var raw = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, raw);
            }

            using var doc = JsonDocument.Parse(raw);
            var users = new List<UserInventoryViewModel>();

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in arr.EnumerateArray())
                {
                    users.Add(new UserInventoryViewModel
                    {
                        Id = u.GetPropertyOrDefault("id"),
                        DisplayName = u.GetPropertyOrDefault("displayName"),
                        UserPrincipalName = u.GetPropertyOrDefault("userPrincipalName"),
                        Mail = u.GetPropertyOrDefault("mail"),
                        JobTitle = u.GetPropertyOrDefault("jobTitle"),
                        Department = u.GetPropertyOrDefault("department"),
                        MobilePhone = u.GetPropertyOrDefault("mobilePhone")
                    });
                }
            }

            string? nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            string? nextSkip = ExtractSkipToken(nextLink ?? string.Empty);

            var model = new PagedUsersViewModel
            {
                Users = users,
                NextSkipToken = nextSkip,
                PageSize = pageSize,
                Term = term
            };

            return View("UsuariosPaged", model);
        }

        // Inventario unificado
        [HttpGet("Equipos")]
        public async Task<IActionResult> Equipos([FromQuery] string? upn = null, [FromQuery] Guid? ubicacionId = null)
        {
            var correo = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(correo))
                return Unauthorized("No se pudo determinar el correo del usuario autenticado.");

            var clienteId = await _clientesService.GetClienteIdByEmailAsync(correo);
            if (clienteId == Guid.Empty)
                return Unauthorized("Cliente no encontrado en Dataverse para el correo autenticado.");

            var categorias = await GetCategoriasAsync();
            var equiposPorCategoria = new Dictionary<Guid, List<EquipoVm>>();
            foreach (var cat in categorias)
                equiposPorCategoria[cat.Id] = await GetEquiposByClienteAndCategoriaAsync(clienteId, cat.Id);

            var usuarios = await GetUsuariosTenantAsync(pageSize: 50);

            var equiposUsuario = new List<EquipoVm>();
            if (!string.IsNullOrWhiteSpace(upn))
                equiposUsuario = await GetEquiposPorUsuarioUpnAsync(upn);

            var ubicaciones = await GetUbicacionesAsync(clienteId);

            var equiposPorUbicacion = new List<EquipoVm>();
            if (ubicacionId.HasValue && ubicacionId.Value != Guid.Empty)
                equiposPorUbicacion = await GetEquiposPorUbicacionAsync(ubicacionId.Value);
var propioOrentaOptions = await GetPropioORentaOptionsAsync();

            var model = new InventarioVm
            {
                ClienteId = clienteId,
                Categorias = categorias,
                EquiposPorCategoria = equiposPorCategoria,
                UsuariosTenant = usuarios,
                UsuarioSeleccionadoUpn = upn,
                EquiposDelUsuarioSeleccionado = equiposUsuario,
                Ubicaciones = ubicaciones,
                UbicacionSeleccionadaId = ubicacionId,
                 EquiposPorUbicacionSeleccionada = equiposPorUbicacion,
                PropioORentaOptions = propioOrentaOptions
            };

            return View("Inventario", model);
        }

        // Crear equipo
        [HttpPost("CrearEquipo")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearEquipo(CrearEquipoVm model)
        {
            if (!ModelState.IsValid)
            {
                TempData["InventarioError"] = "Datos incompletos o inválidos. Verifica el formulario.";
                return RedirectToAction("Equipos");
            }

            var correo = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            var clienteId = await _clientesService.GetClienteIdByEmailAsync(correo ?? string.Empty);
            if (clienteId == Guid.Empty)
            {
                TempData["InventarioError"] = "No se pudo resolver el cliente para el usuario autenticado.";
                return RedirectToAction("Equipos");
            }

           var entity = new Entity(EquiposEntityName);
            entity["cr07a_cliente"] = new EntityReference("cr07a_cliente", clienteId);
            entity["cr07a_categoria"] = new EntityReference("cr07a_categoriasdigitalapp", model.CategoriaId);
            if (!string.IsNullOrWhiteSpace(model.Marca)) entity["cr07a_marca"] = model.Marca;
            if (!string.IsNullOrWhiteSpace(model.Modelo)) entity["cr07a_modelo"] = model.Modelo;
            if (!string.IsNullOrWhiteSpace(model.Serial)) entity["cr07a_serial"] = model.Serial;
            if (!string.IsNullOrWhiteSpace(model.Notas)) entity["cr07a_notas"] = model.Notas;
            if (!string.IsNullOrWhiteSpace(model.AsignadoA)) entity["cr07a_asignadoa"] = model.AsignadoA;
            if (model.UbicacionId.HasValue && model.UbicacionId.Value != Guid.Empty)
                entity["cr07a_ubicacionid"] = new EntityReference("cr07a_ubicacionesdigitalapp", model.UbicacionId.Value);

            if (model.Estado.HasValue)
            {
                var allowed = new[] { 645250000, 645250001, 645250002 };
                if (!allowed.Contains(model.Estado.Value))
                {
                    TempData["InventarioError"] = "Estado inválido. Selecciona un valor permitido.";
                    return RedirectToAction("Equipos");
                }
                entity["cr07a_estado"] = new OptionSetValue(model.Estado.Value);
            }

            if (model.FechaCompra.HasValue) entity["cr07a_fechacompra"] = model.FechaCompra.Value;
 if (model.PropioORenta.HasValue)
            {
                entity[PropioORentaAttribute] = new OptionSetValue(model.PropioORenta.Value);
            }

            try
            {
                 var newId = _dataverse.Create(entity);

                if (model.ActaDeEntrega != null && model.ActaDeEntrega.Length > 0)
                {
                    try
                    {
                        await using var stream = model.ActaDeEntrega.OpenReadStream();
                        var fileName = Path.GetFileName(model.ActaDeEntrega.FileName);
                        await _dataverseFiles.UploadFileAsync(
                            EquiposEntitySetName,
                            newId,
                            ActaColumnName,
                            stream,
                            string.IsNullOrWhiteSpace(fileName) ? $"acta-{newId}.bin" : fileName,
                            model.ActaDeEntrega.ContentType);
                    }
                    catch (Exception ex)
                    {
                        TempData["InventarioError"] = "Equipo creado, pero no se pudo cargar el acta de entrega: " + ex.Message;
                        return RedirectToAction("Equipos");
                    }
                }
                TempData["InventarioOk"] = "Equipo creado exitosamente.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error creando el equipo: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // EDITAR EQUIPO
        // GET para cargar datos del equipo (AJAX)
        [HttpGet("Equipo")]
        public async Task<IActionResult> Equipo([FromQuery] Guid id)
        {
            if (id == Guid.Empty) return BadRequest("id requerido.");

              var entity = await _dataverse.RetrieveAsync(EquiposEntityName, id, new ColumnSet(
                "cr07a_categoria",
                "cr07a_marca",
                "cr07a_modelo",
                "cr07a_serial",
                "cr07a_estado",
                "cr07a_fechacompra",
                "cr07a_notas",
                "cr07a_asignadoa",
               "cr07a_ubicacionid",
                PropioORentaAttribute,
                ActaColumnName
            ));

            var categoriaRef = entity.GetAttributeValue<EntityReference>("cr07a_categoria");
            var ubicRef = entity.GetAttributeValue<EntityReference>("cr07a_ubicacionid");
            var estadoVal = entity.GetAttributeValue<OptionSetValue>("cr07a_estado");
  var propioOrentaVal = entity.GetAttributeValue<OptionSetValue>(PropioORentaAttribute);
            var actaNombre = entity.GetAttributeValue<string>(ActaColumnName);
            var vm = new EditEquipoVm
            {
                Id = id,
                CategoriaId = categoriaRef?.Id ?? Guid.Empty,
                Marca = entity.GetAttributeValue<string>("cr07a_marca") ?? "",
                Modelo = entity.GetAttributeValue<string>("cr07a_modelo") ?? "",
                Serial = entity.GetAttributeValue<string>("cr07a_serial") ?? "",
                Estado = estadoVal?.Value,
                FechaCompra = entity.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                Notas = entity.GetAttributeValue<string>("cr07a_notas") ?? "",
                AsignadoA = entity.GetAttributeValue<string>("cr07a_asignadoa") ?? "",
                  UbicacionId = ubicRef?.Id,
                PropioORenta = propioOrentaVal?.Value,
                ActaDeEntregaNombre = actaNombre,
                TieneActaDeEntrega = !string.IsNullOrWhiteSpace(actaNombre)
            };

            return Json(vm);
        }
[HttpGet("Acta")]
        public async Task<IActionResult> Acta([FromQuery] Guid id)
        {
            if (id == Guid.Empty) return BadRequest("Id requerido.");

            try
            {
                var file = await _dataverseFiles.GetFileAsync(EquiposEntitySetName, id, ActaColumnName);
                if (file == null) return NotFound();
                return File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
            }
            catch (Exception ex)
            {
                return StatusCode((int)HttpStatusCode.InternalServerError, "Error descargando el acta: " + ex.Message);
            }
        }

        // POST actualizar equipo
        [HttpPost("EditarEquipo")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarEquipo(EditEquipoVm model)
        {
            if (!ModelState.IsValid || model.Id == Guid.Empty)
            {
                TempData["InventarioError"] = "Datos inválidos en la edición del equipo.";
                return RedirectToAction("Equipos");
            }

 var entity = new Entity(EquiposEntityName, model.Id);   
          // Campos actualizables
            entity["cr07a_categoria"] = new EntityReference("cr07a_categoriasdigitalapp", model.CategoriaId);
            entity["cr07a_marca"] = model.Marca ?? "";
            entity["cr07a_modelo"] = model.Modelo ?? "";
            entity["cr07a_serial"] = model.Serial ?? "";
            entity["cr07a_notas"] = model.Notas ?? "";
            if (!string.IsNullOrWhiteSpace(model.AsignadoA))
                entity["cr07a_asignadoa"] = model.AsignadoA;
            else
                entity["cr07a_asignadoa"] = null;

            if (model.UbicacionId.HasValue && model.UbicacionId.Value != Guid.Empty)
                entity["cr07a_ubicacionid"] = new EntityReference("cr07a_ubicacionesdigitalapp", model.UbicacionId.Value);
            else
                entity["cr07a_ubicacionid"] = null;

            if (model.Estado.HasValue)
            {
                var allowed = new[] { 645250000, 645250001, 645250002 };
                if (!allowed.Contains(model.Estado.Value))
                {
                    TempData["InventarioError"] = "Estado inválido en edición.";
                    return RedirectToAction("Equipos");
                }
                entity["cr07a_estado"] = new OptionSetValue(model.Estado.Value);
            }
            else
            {
                entity["cr07a_estado"] = null;
            }

            if (model.FechaCompra.HasValue)
                entity["cr07a_fechacompra"] = model.FechaCompra.Value;
            else
                entity["cr07a_fechacompra"] = null;
 if (model.PropioORenta.HasValue)
                entity[PropioORentaAttribute] = new OptionSetValue(model.PropioORenta.Value);
            else
                entity[PropioORentaAttribute] = null;

            try
            {
                _dataverse.Update(entity);
                
                if (model.ActaDeEntrega != null && model.ActaDeEntrega.Length > 0)
                {
                    try
                    {
                        await using var stream = model.ActaDeEntrega.OpenReadStream();
                        var fileName = Path.GetFileName(model.ActaDeEntrega.FileName);
                        await _dataverseFiles.UploadFileAsync(
                            EquiposEntitySetName,
                            model.Id,
                            ActaColumnName,
                            stream,
                            string.IsNullOrWhiteSpace(fileName) ? $"acta-{model.Id}.bin" : fileName,
                            model.ActaDeEntrega.ContentType);
                    }
                    catch (Exception ex)
                    {
                        TempData["InventarioError"] = "Se actualizó el equipo, pero no se pudo cargar el acta de entrega: " + ex.Message;
                        return RedirectToAction("Equipos");
                    }
                }

                TempData["InventarioOk"] = "Equipo actualizado.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error actualizando el equipo: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // DELETE equipo
        [HttpPost("EliminarEquipo")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarEquipo([FromForm] Guid id)
        {
            if (id == Guid.Empty)
            {
                TempData["InventarioError"] = "Id de equipo inválido.";
                return RedirectToAction("Equipos");
            }

            try
            {
                _dataverse.Delete("cr07a_equiposdigitalapp", id);
                TempData["InventarioOk"] = "Equipo eliminado.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error eliminando el equipo: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // UBICACIONES
        [HttpPost("CrearUbicacion")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUbicacion(string nombre, string? descripcion)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                TempData["InventarioError"] = "El nombre de la ubicación es requerido.";
                return RedirectToAction("Equipos");
            }

            var correo = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            var clienteId = await _clientesService.GetClienteIdByEmailAsync(correo ?? string.Empty);
            if (clienteId == Guid.Empty)
            {
                TempData["InventarioError"] = "No se pudo resolver el cliente para el usuario autenticado.";
                return RedirectToAction("Equipos");
            }

            var entity = new Entity("cr07a_ubicacionesdigitalapp");
            entity["cr07a_name"] = nombre;
            if (!string.IsNullOrWhiteSpace(descripcion)) entity["cr07a_descripcion"] = descripcion;
            entity["cr07a_cliente"] = new EntityReference("cr07a_cliente", clienteId);

            try
            {
                _dataverse.Create(entity);
                TempData["InventarioOk"] = "Ubicación creada exitosamente.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error creando la ubicación: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // GET cargar ubicación por id (AJAX)
        [HttpGet("Ubicacion")]
        public async Task<IActionResult> Ubicacion([FromQuery] Guid id)
        {
            if (id == Guid.Empty) return BadRequest("id requerido.");

            var entity = await _dataverse.RetrieveAsync("cr07a_ubicacionesdigitalapp", id, new ColumnSet("cr07a_name", "cr07a_descripcion"));

            return Json(new
            {
                id = id,
                nombre = entity.GetAttributeValue<string>("cr07a_name") ?? "",
                descripcion = entity.GetAttributeValue<string>("cr07a_descripcion") ?? ""
            });
        }

        [HttpPost("EditarUbicacion")]
        [ValidateAntiForgeryToken]
        public IActionResult EditarUbicacion([FromForm] Guid id, [FromForm] string nombre, [FromForm] string? descripcion)
        {
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(nombre))
            {
                TempData["InventarioError"] = "Datos inválidos en la edición de ubicación.";
                return RedirectToAction("Equipos");
            }

            var entity = new Entity("cr07a_ubicacionesdigitalapp", id);
            entity["cr07a_name"] = nombre;
            entity["cr07a_descripcion"] = descripcion ?? "";

            try
            {
                _dataverse.Update(entity);
                TempData["InventarioOk"] = "Ubicación actualizada.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error actualizando la ubicación: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        [HttpPost("EliminarUbicacion")]
        [ValidateAntiForgeryToken]
        public IActionResult EliminarUbicacion([FromForm] Guid id)
        {
            if (id == Guid.Empty)
            {
                TempData["InventarioError"] = "Id de ubicación inválido.";
                return RedirectToAction("Equipos");
            }

            try
            {
                _dataverse.Delete("cr07a_ubicacionesdigitalapp", id);
                TempData["InventarioOk"] = "Ubicación eliminada.";
            }
            catch (Exception ex)
            {
            TempData["InventarioError"] = "Error eliminando la ubicación: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // Navegación por ubicación
        [HttpGet("EquiposPorUbicacion")]
        public IActionResult EquiposPorUbicacion([FromQuery] Guid ubicacionId)
        {
            if (ubicacionId == Guid.Empty)
                return BadRequest("ubicacionId requerido.");
            return RedirectToAction("Equipos", new { ubicacionId });
        }

        // Buscar usuarios (autocompletar UPN)
        [HttpGet("BuscarUsuarios")]
        public async Task<IActionResult> BuscarUsuarios([FromQuery] string term, [FromQuery] int top = 10)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(new { value = Array.Empty<object>() });

            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch (InvalidOperationException ex) { return StatusCode(401, new { error = ex.Message }); }

            var safe = term.Replace("'", "''");
            var url = $"https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'{safe}') or startswith(userPrincipalName,'{safe}')&$select=id,displayName,userPrincipalName,mail&$top={top}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();

            var list = new List<object>();
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in arr.EnumerateArray())
                    {
                        list.Add(new
                        {
                            id = u.GetPropertyOrDefault("id"),
                            displayName = u.GetPropertyOrDefault("displayName"),
                            upn = u.GetPropertyOrDefault("userPrincipalName"),
                            mail = u.GetPropertyOrDefault("mail")
                        });
                    }
                }
            }

            return Json(new { value = list });
        }

        // Modal detalle usuario
        [HttpGet("UsuarioDetalle")]
        public async Task<IActionResult> UsuarioDetalle([FromQuery] string userId, [FromQuery] string upn)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(upn))
                return BadRequest("userId y upn requeridos.");

            var client = await _graphFactory.CreateClientAsync();

            var profileUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userId)}?$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone,businessPhones,officeLocation";
            var profileResp = await client.GetAsync(profileUrl);
            var profileRaw = await profileResp.Content.ReadAsStringAsync();

            var vm = new UserDetailVm();
            if (profileResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(profileRaw);
                var root = doc.RootElement;
                vm.Id = root.GetPropertyOrDefault("id");
                vm.DisplayName = root.GetPropertyOrDefault("displayName");
                vm.UserPrincipalName = root.GetPropertyOrDefault("userPrincipalName");
                vm.Mail = root.GetPropertyOrDefault("mail");
                vm.JobTitle = root.GetPropertyOrDefault("jobTitle");
                vm.Department = root.GetPropertyOrDefault("department");
                vm.MobilePhone = root.GetPropertyOrDefault("mobilePhone");
                vm.BusinessPhones = root.TryGetProperty("businessPhones", out var bp) && bp.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", bp.EnumerateArray().Select(x => x.GetString()))
                    : "";
                vm.OfficeLocation = root.GetPropertyOrDefault("officeLocation");
            }

            var licUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(userId)}/licenseDetails";
            var licResp = await client.GetAsync(licUrl);
            var licRaw = await licResp.Content.ReadAsStringAsync();
            if (licResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(licRaw);
                if (doc.RootElement.TryGetProperty("value", out var licArr) && licArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var lic in licArr.EnumerateArray())
                    {
                        var plans = new List<string>();
                        if (lic.TryGetProperty("servicePlans", out var sp) && sp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var plan in sp.EnumerateArray())
                                plans.Add(plan.GetPropertyOrDefault("servicePlanName"));
                        }
                        vm.Licenses.Add(new LicenseAssignmentViewModel
                        {
                            SkuId = lic.TryGetProperty("skuId", out var s) ? s.ToString() : "",
                            SkuPartNumber = lic.GetPropertyOrDefault("skuPartNumber"),
                            CapabilityStatus = lic.GetPropertyOrDefault("capabilityStatus"),
                            ServicePlans = plans
                        });
                    }
                }
            }

            vm.EquiposAsignados = await GetEquiposPorUsuarioUpnAsync(upn);
            return PartialView("_UsuarioDetalle", vm);
        }

        // ---------- UsuariosPage para paginación incremental en pestaña Inventario/Usuarios ----------
        // GET /Inventario/UsuariosPage?pageSize=25&skipToken=...
        [HttpGet("UsuariosPage")]
        public async Task<IActionResult> UsuariosPage([FromQuery] int pageSize = 25, [FromQuery] string? skipToken = null)
        {
            if (pageSize <= 0) pageSize = 25;
            if (pageSize > 999) pageSize = 999;

            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch (InvalidOperationException ex) { return StatusCode(401, new { error = ex.Message }); }

            var url = $"https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone&$top={pageSize}";
            if (!string.IsNullOrEmpty(skipToken))
            {
                url += $"&$skiptoken={Uri.EscapeDataString(skipToken)}";
            }

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, raw);

            using var doc = JsonDocument.Parse(raw);

            var users = new List<object>();
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in arr.EnumerateArray())
                {
                    users.Add(new
                    {
                        id = u.GetPropertyOrDefault("id"),
                        displayName = u.GetPropertyOrDefault("displayName"),
                        upn = u.GetPropertyOrDefault("userPrincipalName"),
                        mail = u.GetPropertyOrDefault("mail"),
                        department = u.GetPropertyOrDefault("department")
                    });
                }
            }

            string? nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            string? nextSkipToken = ExtractSkipToken(nextLink ?? string.Empty);

            return Json(new
            {
                users,
                nextSkipToken,
                pageSize
            });
        }

        // ===================== NUEVO: Reportes (JSON) =====================
        [HttpGet("ReportesData")]
        public async Task<IActionResult> ReportesData()
        {
            var correo = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(correo))
                return Unauthorized(new { error = "No se pudo determinar el correo del usuario autenticado." });

            var clienteId = await _clientesService.GetClienteIdByEmailAsync(correo);
            if (clienteId == Guid.Empty)
                return Unauthorized(new { error = "Cliente no encontrado en Dataverse para el correo autenticado." });

            // Todos los equipos del cliente
            var equipos = await GetEquiposByClienteAsync(clienteId);

            // Catálogos para nombres de categorías
            var categorias = await GetCategoriasAsync();
            var catDict = categorias.ToDictionary(x => x.Id, x => x.Nombre);

            // Dispositivos por Categoría
            var porCategoria = equipos
                .GroupBy(e => e.CategoriaId)
                .Select(g => new ReportItem
                {
                    Label = (g.Key != Guid.Empty && catDict.TryGetValue(g.Key, out var name)) ? name : "(Sin categoría)",
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            // Dispositivos por Ubicación (usa texto ya formateado en e.Ubicacion)
            var porUbicacion = equipos
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Ubicacion) ? "(Sin ubicación)" : e.Ubicacion)
                .Select(g => new ReportItem
                {
                    Label = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            // Dispositivos por Marca
            var porMarca = equipos
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Marca) ? "(Sin marca)" : e.Marca)
                .Select(g => new ReportItem
                {
                    Label = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .ToList();

            // ---------- Usuarios sin licencia (Graph) ----------
            int usuariosSinLicencia = 0;
            string? usuariosSinLicenciaWarning = null;
            List<UnlicensedUserSummary> usuariosSinLicenciaListado = new();
            try
            {
                var http = await _graphFactory.CreateClientAsync();
                (usuariosSinLicencia, usuariosSinLicenciaWarning, usuariosSinLicenciaListado) = await CountUsuariosSinLicenciaAsync(http, HttpContext.RequestAborted, MaxUsuariosSinLicenciaListado);
            }
            catch (InvalidOperationException ex)
            {
                usuariosSinLicencia = 0;
                usuariosSinLicenciaWarning = $"No se pudo inicializar el cliente de Microsoft Graph: {ex.Message}";
                usuariosSinLicenciaListado = new List<UnlicensedUserSummary>();
            }
            catch (Exception)
            {
                usuariosSinLicencia = 0;
                usuariosSinLicenciaWarning = "Ocurrió un error inesperado al preparar la consulta a Microsoft Graph.";
                usuariosSinLicenciaListado = new List<UnlicensedUserSummary>();
            }

            var usuariosSinLicenciaOrdenados = usuariosSinLicenciaListado
                .OrderBy(u => string.IsNullOrWhiteSpace(u.DisplayName) ? 1 : 0)
                .ThenBy(
                    u => string.IsNullOrWhiteSpace(u.DisplayName)
                        ? u.UserPrincipalName ?? string.Empty
                        : u.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.UserPrincipalName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Json(new
            {
                porCategoria,
                porUbicacion,
                porMarca,

                usuariosSinLicencia,
                usuariosSinLicenciaWarning,
                usuariosSinLicenciaListado = usuariosSinLicenciaOrdenados
                    .Select(u => new
                    {
                        displayName = u.DisplayName,
                        userPrincipalName = u.UserPrincipalName,
                        mail = u.Mail,
                        department = u.Department
                    })
                    .ToList()
            });
        }
        private static async Task<(int Count, string? Warning, List<UnlicensedUserSummary> Usuarios)> CountUsuariosSinLicenciaAsync(HttpClient httpClient, CancellationToken cancellationToken, int maxUsersToCollect)
        {
            try
            {
                var advanced = await TryCountUsuariosSinLicenciaConFiltroAsync(httpClient, cancellationToken, maxUsersToCollect);
                if (advanced.Success)
                {
                    return (advanced.Count, null, advanced.Users);
                }

                if (!advanced.ShouldFallback)
                {
                    return (0, advanced.Warning, advanced.Users);
                }

                var fallback = await CountUsuariosSinLicenciaEnumerandoAsync(httpClient, cancellationToken, maxUsersToCollect);

                if (fallback.Warning is null)
                {
                    fallback.Warning = advanced.Warning;
                }
                else if (!string.IsNullOrWhiteSpace(advanced.Warning))
                {
                    fallback.Warning = $"{advanced.Warning} {fallback.Warning}";
                }

                return fallback;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (0, "La consulta de usuarios sin licencia fue cancelada antes de completarse.", new List<UnlicensedUserSummary>());
            }
            catch (HttpRequestException)
            {
                return (0, "No se pudo contactar Microsoft Graph para obtener los usuarios sin licencia. Verifica la conectividad o renueva la sesión.", new List<UnlicensedUserSummary>());
            }
            catch (JsonException)
            {
                return (0, "Microsoft Graph devolvió una respuesta inesperada al consultar usuarios sin licencia.", new List<UnlicensedUserSummary>());
            }
        }

        private static async Task<(bool Success, int Count, string? Warning, bool ShouldFallback, List<UnlicensedUserSummary> Users)> TryCountUsuariosSinLicenciaConFiltroAsync(HttpClient httpClient, CancellationToken cancellationToken, int maxUsersToCollect)
        {
            var filter = Uri.EscapeDataString("assignedLicenses/$count eq 0");
            string? nextUrl = $"https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,department&$filter={filter}&$top=999&$count=true";
            var countFromOData = false;
            var total = 0;
            var users = new List<UnlicensedUserSummary>();

            while (!string.IsNullOrEmpty(nextUrl))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
                request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=999");

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var detail = TryExtractGraphErrorDetail(raw);

                    var baseWarning = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "El token para Microsoft Graph no es válido (401). Inicia sesión nuevamente para renovarlo.",
                        HttpStatusCode.Forbidden => "Microsoft Graph devolvió 403 (Forbidden). Confirma que la aplicación tenga el consentimiento de administrador para User.Read.All o Directory.Read.All.",
                        _ => $"Microsoft Graph devolvió {(int)response.StatusCode}."
                    };

                    var warning = detail is not null
                        ? $"{baseWarning} Detalle: {detail}"
                        : $"{baseWarning} Revisa el registro del servidor para más detalles.";

                    var shouldFallback = response.StatusCode == HttpStatusCode.BadRequest
                        && detail is not null
                        && detail.IndexOf("filter property that is not indexed", StringComparison.OrdinalIgnoreCase) >= 0;

                    return (false, 0, shouldFallback
                        ? $"{warning} Se intentará un método alternativo para contar los usuarios sin licencia."
                        : warning, shouldFallback, users);
                }

                using var document = JsonDocument.Parse(raw);

                if (!countFromOData && document.RootElement.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
                {
                    total += valueElement.GetArrayLength();

                    foreach (var user in valueElement.EnumerateArray())
                    {
                        if (users.Count >= maxUsersToCollect)
                        {
                            break;
                        }

                        users.Add(new UnlicensedUserSummary(
                            user.GetPropertyOrDefault("displayName"),
                            user.GetPropertyOrDefault("userPrincipalName"),
                            user.GetPropertyOrDefault("mail"),
                            user.GetPropertyOrDefault("department")));
                    }
                }

                if (document.RootElement.TryGetProperty("@odata.count", out var countElement) && countElement.ValueKind == JsonValueKind.Number)
                {
                    if (countElement.TryGetInt32(out var count32))
                    {
                        total = count32;
                        countFromOData = true;
                    }
                    else if (countElement.TryGetInt64(out var count64))
                    {
                        total = count64 > int.MaxValue ? int.MaxValue : (int)count64;
                        countFromOData = true;
                    }
                }

                if (countFromOData)
                {
                    break;
                }

                if (document.RootElement.TryGetProperty("@odata.nextLink", out var nextElement) && nextElement.ValueKind == JsonValueKind.String)
                {
                    nextUrl = nextElement.GetString();
                }
                else
                {
                    nextUrl = null;
                }
            }

            return (true, total, null, false, users);
        }

        private static async Task<(int Count, string? Warning, List<UnlicensedUserSummary> Users)> CountUsuariosSinLicenciaEnumerandoAsync(HttpClient httpClient, CancellationToken cancellationToken, int maxUsersToCollect)
        {
            var total = 0;
            string? nextUrl = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,department,assignedLicenses&$top=999";
            var users = new List<UnlicensedUserSummary>();

            while (!string.IsNullOrEmpty(nextUrl))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=999");

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var detail = TryExtractGraphErrorDetail(raw);

                    var baseWarning = response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "El token para Microsoft Graph no es válido (401). Inicia sesión nuevamente para renovarlo.",
                        HttpStatusCode.Forbidden => "Microsoft Graph devolvió 403 (Forbidden). Confirma que la aplicación tenga el consentimiento de administrador para User.Read.All o Directory.Read.All.",
                        _ => $"Microsoft Graph devolvió {(int)response.StatusCode}."
                    };

                    var warning = detail is not null
                        ? $"{baseWarning} Detalle: {detail}"
                        : $"{baseWarning} Revisa el registro del servidor para más detalles.";

                    return (0, warning, users);
                }

                using var document = JsonDocument.Parse(raw);

                if (document.RootElement.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var user in valueElement.EnumerateArray())
                    {
                        if (!user.TryGetProperty("assignedLicenses", out var licenses) || licenses.ValueKind != JsonValueKind.Array || licenses.GetArrayLength() == 0)
                        {
                            total++;

                            if (users.Count < maxUsersToCollect)
                            {
                                users.Add(new UnlicensedUserSummary(
                                    user.GetPropertyOrDefault("displayName"),
                                    user.GetPropertyOrDefault("userPrincipalName"),
                                    user.GetPropertyOrDefault("mail"),
                                    user.GetPropertyOrDefault("department")));
                            }
                        }
                    }
                }

                if (document.RootElement.TryGetProperty("@odata.nextLink", out var nextElement) && nextElement.ValueKind == JsonValueKind.String)
                {
                    nextUrl = nextElement.GetString();
                }
                else
                {
                    nextUrl = null;
                }
            }

            return (total, "Se utilizó un método alternativo enumerando usuarios porque el filtro avanzado de Microsoft Graph no está disponible.", users);
        }

        private static string? TryExtractGraphErrorDetail(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                using var errorDoc = JsonDocument.Parse(raw);
                if (!errorDoc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    return null;
                }

                if (errorElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    return messageElement.GetString();
                }

                if (errorElement.TryGetProperty("innerError", out var innerElement)
                    && innerElement.TryGetProperty("message", out var innerMessage)
                    && innerMessage.ValueKind == JsonValueKind.String)
                {
                    return innerMessage.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

            // ===================== NUEVO: Exportación CSV =====================
                [HttpGet("ExportEquiposCsv")]
        public async Task<IActionResult> ExportEquiposCsv()
        {
            var correo = User.FindFirst("preferred_username")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(correo))
                return Unauthorized("No se pudo determinar el correo del usuario autenticado.");

            var clienteId = await _clientesService.GetClienteIdByEmailAsync(correo);
            if (clienteId == Guid.Empty)
                return Unauthorized("Cliente no encontrado en Dataverse para el correo autenticado.");

            var equipos = await GetEquiposByClienteAsync(clienteId);

            var sb = new StringBuilder();
            sb.AppendLine("Marca,Modelo,Serial,Estado,FechaCompra,Ubicacion,AsignadoA,Notas,CategoriaId,UbicacionId,EquipoId");

            foreach (var e in equipos)
            {
                static string Csv(string? s)
                {
                    s ??= string.Empty;
                    if (s.Contains('"')) s = s.Replace("\"", "\"\"");
                    if (s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                        s = $"\"{s}\"";
                    return s;
                }

                sb.AppendLine(string.Join(",",
                    Csv(e.Marca),
                    Csv(e.Modelo),
                    Csv(e.Serial),
                    Csv(e.Estado),
                    Csv(e.FechaCompra.HasValue ? e.FechaCompra.Value.ToString("yyyy-MM-dd") : ""),
                    Csv(e.Ubicacion),
                    Csv(e.AsignadoA),
                    Csv(e.Notas),
                    e.CategoriaId.ToString(),
                    e.UbicacionId.HasValue ? e.UbicacionId.Value.ToString() : "",
                    e.Id.ToString()
                ));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"equipos_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // -------- Privados (Dataverse) --------
        private async Task<List<CategoriaVm>> GetCategoriasAsync()
        {
            var q = new QueryExpression("cr07a_categoriasdigitalapp")
            {
                ColumnSet = new ColumnSet("cr07a_categoriasdigitalappid", "cr07a_name")
            };
            var result = await _dataverse.RetrieveMultipleAsync(q);
            return result.Entities.Select(c => new CategoriaVm
            {
                Id = c.Id,
                Nombre = c.GetAttributeValue<string>("cr07a_name") ?? ""
            }).ToList();
        }

        private async Task<List<EquipoVm>> GetEquiposByClienteAndCategoriaAsync(Guid clienteId, Guid categoriaId)
        {
            var q = new QueryExpression(EquiposEntityName)
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacion",
                    "cr07a_notas",
                    "cr07a_categoria",
                    "cr07a_cliente",
                    "cr07a_asignadoa",
                    "cr07a_ubicacionid",
                    PropioORentaAttribute,
                    ActaColumnName
                )
            };
            q.Criteria.AddCondition("cr07a_cliente", ConditionOperator.Equal, clienteId);
            q.Criteria.AddCondition("cr07a_categoria", ConditionOperator.Equal, categoriaId);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var clienteRef = e.GetAttributeValue<EntityReference>("cr07a_cliente");
                var categoriaRef = e.GetAttributeValue<EntityReference>("cr07a_categoria");
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicRef = e.GetAttributeValue<EntityReference>("cr07a_ubicacionid");
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid")
                    ? e.FormattedValues["cr07a_ubicacionid"]
                    : e.GetAttributeValue<string>("cr07a_ubicacion") ?? "";
  var propioFmt = e.FormattedValues.ContainsKey(PropioORentaAttribute)
                    ? e.FormattedValues[PropioORentaAttribute]
                    : string.Empty;
                var propioValue = e.GetAttributeValue<OptionSetValue>(PropioORentaAttribute)?.Value;
                var actaNombre = e.GetAttributeValue<string>(ActaColumnName);
                list.Add(new EquipoVm
                {
                    Id = e.Id,
                    ClienteId = clienteRef?.Id ?? Guid.Empty,
                    CategoriaId = categoriaRef?.Id ?? Guid.Empty,
                    Marca = e.GetAttributeValue<string>("cr07a_marca") ?? "",
                    Modelo = e.GetAttributeValue<string>("cr07a_modelo") ?? "",
                    Serial = e.GetAttributeValue<string>("cr07a_serial") ?? "",
                    Estado = estadoFmt,
                    FechaCompra = e.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                    Ubicacion = ubicFmt,
                    Notas = e.GetAttributeValue<string>("cr07a_notas") ?? "",
                    AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? "",
                    UbicacionId = ubicRef?.Id,
                    PropioORentaValue = propioValue,
                    PropioORentaLabel = string.IsNullOrWhiteSpace(propioFmt) ? null : propioFmt,
                    TieneActaDeEntrega = !string.IsNullOrWhiteSpace(actaNombre),
                    ActaDeEntregaNombre = actaNombre
                });
            }
            await PopulateAsignadoNombresAsync(list);
            return list;
        }

        // NUEVO: Todos los equipos del cliente (una sola consulta)
        private async Task<List<EquipoVm>> GetEquiposByClienteAsync(Guid clienteId)
        {
            var q = new QueryExpression(EquiposEntityName)
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacion",
                    "cr07a_notas",
                    "cr07a_categoria",
                    "cr07a_cliente",
                    "cr07a_asignadoa",
 "cr07a_ubicacionid",
                    PropioORentaAttribute,
                    ActaColumnName                )
            };
            q.Criteria.AddCondition("cr07a_cliente", ConditionOperator.Equal, clienteId);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var clienteRef = e.GetAttributeValue<EntityReference>("cr07a_cliente");
                var categoriaRef = e.GetAttributeValue<EntityReference>("cr07a_categoria");
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicRef = e.GetAttributeValue<EntityReference>("cr07a_ubicacionid");
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid")
                    ? e.FormattedValues["cr07a_ubicacionid"]
                    : e.GetAttributeValue<string>("cr07a_ubicacion") ?? "";
 var propioFmt = e.FormattedValues.ContainsKey(PropioORentaAttribute)
                    ? e.FormattedValues[PropioORentaAttribute]
                    : string.Empty;
                var propioValue = e.GetAttributeValue<OptionSetValue>(PropioORentaAttribute)?.Value;
                var actaNombre = e.GetAttributeValue<string>(ActaColumnName);
                list.Add(new EquipoVm
                {
                    Id = e.Id,
                    ClienteId = clienteRef?.Id ?? Guid.Empty,
                    CategoriaId = categoriaRef?.Id ?? Guid.Empty,
                    Marca = e.GetAttributeValue<string>("cr07a_marca") ?? "",
                    Modelo = e.GetAttributeValue<string>("cr07a_modelo") ?? "",
                    Serial = e.GetAttributeValue<string>("cr07a_serial") ?? "",
                    Estado = estadoFmt,
                    FechaCompra = e.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                    Ubicacion = ubicFmt,
                    Notas = e.GetAttributeValue<string>("cr07a_notas") ?? "",
                    AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? "",
   UbicacionId = ubicRef?.Id,
                    PropioORentaValue = propioValue,
                    PropioORentaLabel = string.IsNullOrWhiteSpace(propioFmt) ? null : propioFmt,
                    TieneActaDeEntrega = !string.IsNullOrWhiteSpace(actaNombre),
                    ActaDeEntregaNombre = actaNombre                });
            }
                        await PopulateAsignadoNombresAsync(list);

            return list;
        }
  private async Task PopulateAsignadoNombresAsync(List<EquipoVm> equipos)
        {
            if (equipos == null || equipos.Count == 0) return;

            var upns = equipos
                .Select(e => e.AsignadoA?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (upns.Count == 0) return;

            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch (InvalidOperationException)
            {
                foreach (var eq in equipos)
                {
                    if (!string.IsNullOrWhiteSpace(eq.AsignadoA))
                        eq.AsignadoNombre = eq.AsignadoA;
                }
                return;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var requestId = 1;

            foreach (var chunk in upns.Chunk(20))
            {
                var requests = new List<object>(chunk.Length);
                foreach (var upn in chunk)
                {
                    if (upn == null) continue;
                    requests.Add(new
                    {
                        id = (requestId++).ToString(),
                        method = "GET",
                        url = $"/users/{Uri.EscapeDataString(upn)}?$select=displayName,userPrincipalName"
                    });
                }

                if (requests.Count == 0) continue;

                var payload = JsonSerializer.Serialize(new { requests });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync("https://graph.microsoft.com/v1.0/$batch", content);
                if (!response.IsSuccessStatusCode) continue;

                var raw = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(raw);

                if (!doc.RootElement.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in responses.EnumerateArray())
                {
                    if (!item.TryGetProperty("status", out var statusEl)) continue;
                    var status = statusEl.GetInt32();
                    if (status < 200 || status >= 300) continue;
                    if (!item.TryGetProperty("body", out var body)) continue;
                    var upn = body.GetPropertyOrDefault("userPrincipalName");
                    var displayName = body.GetPropertyOrDefault("displayName");
                    if (!string.IsNullOrWhiteSpace(upn) && !string.IsNullOrWhiteSpace(displayName))
                    {
                        map[upn] = displayName;
                    }
                }
            }

            foreach (var equipo in equipos)
            {
                if (string.IsNullOrWhiteSpace(equipo.AsignadoA)) continue;
                if (map.TryGetValue(equipo.AsignadoA, out var display))
                {
                    equipo.AsignadoNombre = display;
                }
                else
                {
                    equipo.AsignadoNombre = equipo.AsignadoA;
                }
            }
        }

        private async Task<List<OptionItemVm>> GetPropioORentaOptionsAsync()
        {
            try
            {
                var request = new RetrieveAttributeRequest
                {
                    EntityLogicalName = EquiposEntityName,
                    LogicalName = PropioORentaAttribute,
                    RetrieveAsIfPublished = true
                };

                var response = (RetrieveAttributeResponse)await _dataverse.ExecuteAsync(request);
                if (response.AttributeMetadata is EnumAttributeMetadata enumMetadata)
                {
                    var options = new List<OptionItemVm>();
                    foreach (var opt in enumMetadata.OptionSet.Options)
                    {
                        if (!opt.Value.HasValue) continue;
                        var label = opt.Label?.UserLocalizedLabel?.Label
                                    ?? opt.Label?.LocalizedLabels?.FirstOrDefault()?.Label
                                    ?? opt.Value.Value.ToString();
                        options.Add(new OptionItemVm
                        {
                            Value = opt.Value.Value,
                            Label = label
                        });
                    }

                    return options.OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
            }
            catch
            {
                // Ignorar errores: la vista manejará la ausencia de opciones.
            }

            return new List<OptionItemVm>();
        }

        private async Task<List<UserInventoryViewModel>> GetUsuariosTenantAsync(int pageSize = 50)
        {
            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch (InvalidOperationException ex)
            {
                TempData["InventarioError"] = "No fue posible cargar usuarios del tenant: " + ex.Message;
                return new List<UserInventoryViewModel>();
            }

            var url = $"https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone&$top={pageSize}";
            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();

            var usuarios = new List<UserInventoryViewModel>();
            using var doc = JsonDocument.Parse(raw);

            if (resp.IsSuccessStatusCode &&
                doc.RootElement.TryGetProperty("value", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var u in arr.EnumerateArray())
                {
                    usuarios.Add(new UserInventoryViewModel
                    {
                        Id = u.GetPropertyOrDefault("id"),
                        DisplayName = u.GetPropertyOrDefault("displayName"),
                        UserPrincipalName = u.GetPropertyOrDefault("userPrincipalName"),
                        Mail = u.GetPropertyOrDefault("mail"),
                        JobTitle = u.GetPropertyOrDefault("jobTitle"),
                        Department = u.GetPropertyOrDefault("department"),
                        MobilePhone = u.GetPropertyOrDefault("mobilePhone")
                    });
                }
            }

            return usuarios;
        }

        private async Task<List<UbicacionVm>> GetUbicacionesAsync(Guid clienteId)
        {
            var q = new QueryExpression("cr07a_ubicacionesdigitalapp")
            {
                ColumnSet = new ColumnSet("cr07a_ubicacionesdigitalappid", "cr07a_name", "cr07a_descripcion", "cr07a_cliente")
            };
            q.Criteria.AddCondition("cr07a_cliente", ConditionOperator.Equal, clienteId);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<UbicacionVm>(result.Entities.Count);
            foreach (var u in result.Entities)
            {
                list.Add(new UbicacionVm
                {
                    Id = u.Id,
                    Nombre = u.GetAttributeValue<string>("cr07a_name") ?? "",
                    Descripcion = u.GetAttributeValue<string>("cr07a_descripcion")
                });
            }
            return list;
        }

        private async Task<List<EquipoVm>> GetEquiposPorUbicacionAsync(Guid ubicacionId)
        {
            var q = new QueryExpression(EquiposEntityName)
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacionid",
                    "cr07a_notas",
                      "cr07a_asignadoa",
                    PropioORentaAttribute,
                    ActaColumnName
                )
            };
            q.Criteria.AddCondition("cr07a_ubicacionid", ConditionOperator.Equal, ubicacionId);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid") ? e.FormattedValues["cr07a_ubicacionid"] : "";
                var propioFmt = e.FormattedValues.ContainsKey(PropioORentaAttribute)
                    ? e.FormattedValues[PropioORentaAttribute]
                    : string.Empty;
                var propioValue = e.GetAttributeValue<OptionSetValue>(PropioORentaAttribute)?.Value;
                var actaNombre = e.GetAttributeValue<string>(ActaColumnName);
                list.Add(new EquipoVm
                {
                    Id = e.Id,
                    Marca = e.GetAttributeValue<string>("cr07a_marca") ?? "",
                    Modelo = e.GetAttributeValue<string>("cr07a_modelo") ?? "",
                    Serial = e.GetAttributeValue<string>("cr07a_serial") ?? "",
                    Estado = estadoFmt,
                    FechaCompra = e.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                    Ubicacion = ubicFmt,
                    Notas = e.GetAttributeValue<string>("cr07a_notas") ?? "",
                     AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? "",
                    PropioORentaValue = propioValue,
                    PropioORentaLabel = string.IsNullOrWhiteSpace(propioFmt) ? null : propioFmt,
                    TieneActaDeEntrega = !string.IsNullOrWhiteSpace(actaNombre),
                    ActaDeEntregaNombre = actaNombre
                });
            }
            return list;
        }

        private async Task<List<EquipoVm>> GetEquiposPorUsuarioUpnAsync(string upn)
        {
            var q = new QueryExpression(EquiposEntityName)
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacionid",
                    "cr07a_notas",
                      "cr07a_asignadoa",
                    PropioORentaAttribute,
                    ActaColumnName
                )
            };
            q.Criteria.AddCondition("cr07a_asignadoa", ConditionOperator.Equal, upn);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid") ? e.FormattedValues["cr07a_ubicacionid"] : "";
                var propioFmt = e.FormattedValues.ContainsKey(PropioORentaAttribute)
                    ? e.FormattedValues[PropioORentaAttribute]
                    : string.Empty;
                var propioValue = e.GetAttributeValue<OptionSetValue>(PropioORentaAttribute)?.Value;
                var actaNombre = e.GetAttributeValue<string>(ActaColumnName);
                list.Add(new EquipoVm
                {
                    Id = e.Id,
                    Marca = e.GetAttributeValue<string>("cr07a_marca") ?? "",
                    Modelo = e.GetAttributeValue<string>("cr07a_modelo") ?? "",
                    Serial = e.GetAttributeValue<string>("cr07a_serial") ?? "",
                    Estado = estadoFmt,
                    FechaCompra = e.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                    Ubicacion = ubicFmt,
                    Notas = e.GetAttributeValue<string>("cr07a_notas") ?? "",
                    AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? "",
                    PropioORentaValue = propioValue,
                    PropioORentaLabel = string.IsNullOrWhiteSpace(propioFmt) ? null : propioFmt,
                    TieneActaDeEntrega = !string.IsNullOrWhiteSpace(actaNombre),
                    ActaDeEntregaNombre = actaNombre
                });
            }
                        await PopulateAsignadoNombresAsync(list);

            return list;
        }

        // Helper para extraer el skiptoken de @odata.nextLink (dentro del controlador para evitar errores de ámbito)
        private static string? ExtractSkipToken(string nextLink)
        {
            if (string.IsNullOrEmpty(nextLink)) return null;
            try
            {
                var uri = new Uri(nextLink);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var token = query["$skiptoken"];
                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch
            {
                // Fallback simple si no es una URL válida
                var idx = nextLink.IndexOf("$skiptoken=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var val = nextLink.Substring(idx + "$skiptoken=".Length);
                    return Uri.UnescapeDataString(val);
                }
                return null;
            }
        }
    }

    internal static class JsonExt
    {
        public static string GetPropertyOrDefault(this JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p))
            {
                if (p.ValueKind == JsonValueKind.String) return p.GetString() ?? "";
                if (p.ValueKind == JsonValueKind.Null) return "";
                return p.ToString();
            }
            return "";
        }
    }
}
