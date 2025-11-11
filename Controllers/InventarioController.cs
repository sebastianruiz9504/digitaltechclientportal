using DigitalTechClientPortal.Models;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Generic;
using System.Text;

namespace DigitalTechClientPortal.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class InventarioController : Controller
    {
        private readonly GraphClientFactory _graphFactory;
        private readonly ServiceClient _dataverse;
        private readonly ClientesService _clientesService;

        public InventarioController(GraphClientFactory graphFactory, ServiceClient dataverse, ClientesService clientesService)
        {
            _graphFactory = graphFactory;
            _dataverse = dataverse;
            _clientesService = clientesService;
        }

        // =======================
        //      USUARIOS (vista)
        // =======================
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
                url = $"https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone&$top={pageSize}";
            else
                url = $"https://graph.microsoft.com/v1.0/users?$filter=startswith(displayName,'{term.Replace("'", "''")}')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,mobilePhone&$top={pageSize}";

            if (!string.IsNullOrEmpty(skiptoken))
                url += $"&$skiptoken={Uri.EscapeDataString(skiptoken)}";

            var resp = await client.GetAsync(url);
            var raw = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, raw);

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

        // =======================
        //   INVENTARIO unificado
        // =======================
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
                EquiposPorUbicacionSeleccionada = equiposPorUbicacion
            };

            return View("Inventario", model);
        }

        // =======================
        //        REPORTES
        // =======================

        // Devuelve datos agregados para las gráficas de la pestaña "Reportes"
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
                return Unauthorized(new { error = "Cliente no encontrado para el usuario autenticado." });

            // 1) Equipos del cliente (todos)
            var equiposAll = await GetEquiposByClienteAsync(clienteId);

            // 2) Por Categoría
            var porCategoria = equiposAll
                .GroupBy(e => e.CategoriaId)
                .Select(g => new
                {
                    label = ResolveCategoriaNombre(g.Key, null), // nombre se resolverá luego si es posible
                    categoriaId = g.Key,
                    count = g.Count()
                })
                .ToList();

            // completar nombres de categoría con una consulta real (por si ResolveCategoriaNombre retorna vacío en este punto)
            var categorias = await GetCategoriasAsync();
            var catDict = categorias.ToDictionary(x => x.Id, x => x.Nombre);
            foreach (var c in porCategoria)
            {
                if (string.IsNullOrWhiteSpace(c.label) && catDict.TryGetValue(c.categoriaId, out var n))
                {
                    c.GetType().GetProperty("label")?.SetValue(c, n);
                }
            }

            // 3) Por Ubicación (usa string Ubicacion resultante/format)
            var porUbicacion = equiposAll
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Ubicacion) ? "(Sin ubicación)" : e.Ubicacion)
                .Select(g => new { label = g.Key, count = g.Count() })
                .ToList();

            // 4) Por Marca
            var porMarca = equiposAll
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Marca) ? "(Sin marca)" : e.Marca)
                .Select(g => new { label = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(30) // evita gráficas enormes
                .ToList();

            // 5) Usuarios sin licencia asignada (Graph)
            var sinLicencia = await CountUsuariosSinLicenciaAsync();

            return Json(new
            {
                porCategoria,
                porUbicacion,
                porMarca,
                usuariosSinLicencia = sinLicencia
            });
        }

        // Exporta CSV con todos los equipos del cliente autenticado
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
            sb.AppendLine("Id,ClienteId,CategoriaId,Marca,Modelo,Serial,Estado,FechaCompra,Ubicacion,AsignadoA,Notas");

            foreach (var e in equipos)
            {
                string CsvEsc(string? v) =>
                    "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";

                sb.Append(e.Id).Append(',')
                  .Append(e.ClienteId).Append(',')
                  .Append(e.CategoriaId).Append(',')
                  .Append(CsvEsc(e.Marca)).Append(',')
                  .Append(CsvEsc(e.Modelo)).Append(',')
                  .Append(CsvEsc(e.Serial)).Append(',')
                  .Append(CsvEsc(e.Estado)).Append(',')
                  .Append(e.FechaCompra.HasValue ? e.FechaCompra.Value.ToString("yyyy-MM-dd") : "")
                  .Append(',')
                  .Append(CsvEsc(e.Ubicacion)).Append(',')
                  .Append(CsvEsc(e.AsignadoA)).Append(',')
                  .Append(CsvEsc(e.Notas))
                  .AppendLine();
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv; charset=utf-8", "inventario.csv");
        }

        // =======================
        //      CRUD EQUIPOS
        // =======================

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

            var entity = new Entity("cr07a_equiposdigitalapp");
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

            try
            {
                _dataverse.Create(entity);
                TempData["InventarioOk"] = "Equipo creado exitosamente.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error creando el equipo: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

        // GET para cargar datos del equipo (AJAX)
        [HttpGet("Equipo")]
        public async Task<IActionResult> Equipo([FromQuery] Guid id)
        {
            if (id == Guid.Empty) return BadRequest("id requerido.");

            var entity = await _dataverse.RetrieveAsync("cr07a_equiposdigitalapp", id, new ColumnSet(
                "cr07a_categoria",
                "cr07a_marca",
                "cr07a_modelo",
                "cr07a_serial",
                "cr07a_estado",
                "cr07a_fechacompra",
                "cr07a_notas",
                "cr07a_asignadoa",
                "cr07a_ubicacionid"
            ));

            var categoriaRef = entity.GetAttributeValue<EntityReference>("cr07a_categoria");
            var ubicRef = entity.GetAttributeValue<EntityReference>("cr07a_ubicacionid");
            var estadoVal = entity.GetAttributeValue<OptionSetValue>("cr07a_estado");

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
                UbicacionId = ubicRef?.Id
            };

            return Json(vm);
        }

        [HttpPost("EditarEquipo")]
        [ValidateAntiForgeryToken]
        public IActionResult EditarEquipo(EditEquipoVm model)
        {
            if (!ModelState.IsValid || model.Id == Guid.Empty)
            {
                TempData["InventarioError"] = "Datos inválidos en la edición del equipo.";
                return RedirectToAction("Equipos");
            }

            var entity = new Entity("cr07a_equiposdigitalapp", model.Id);
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

            try
            {
                _dataverse.Update(entity);
                TempData["InventarioOk"] = "Equipo actualizado.";
            }
            catch (Exception ex)
            {
                TempData["InventarioError"] = "Error actualizando el equipo: " + ex.Message;
            }

            return RedirectToAction("Equipos");
        }

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

        // =======================
        //       UBICACIONES
        // =======================

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

        [HttpGet("EquiposPorUbicacion")]
        public IActionResult EquiposPorUbicacion([FromQuery] Guid ubicacionId)
        {
            if (ubicacionId == Guid.Empty)
                return BadRequest("ubicacionId requerido.");
            return RedirectToAction("Equipos", new { ubicacionId });
        }

        // =======================
        //        BÚSQUEDAS
        // =======================

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

        // =======================
        //        PRIVADOS
        // =======================

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

        private static string ResolveCategoriaNombre(Guid categoriaId, List<CategoriaVm>? categorias)
        {
            if (categoriaId == Guid.Empty || categorias == null) return string.Empty;
            return categorias.FirstOrDefault(x => x.Id == categoriaId)?.Nombre ?? string.Empty;
        }

        private async Task<List<EquipoVm>> GetEquiposByClienteAndCategoriaAsync(Guid clienteId, Guid categoriaId)
        {
            var q = new QueryExpression("cr07a_equiposdigitalapp")
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
                    "cr07a_ubicacionid"
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
                    UbicacionId = ubicRef?.Id
                });
            }
            return list;
        }

        // NUEVO: traer todos los equipos del cliente (todas las categorías)
        private async Task<List<EquipoVm>> GetEquiposByClienteAsync(Guid clienteId)
        {
            var q = new QueryExpression("cr07a_equiposdigitalapp")
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
                    "cr07a_ubicacionid"
                )
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
                    UbicacionId = ubicRef?.Id
                });
            }
            return list;
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
            var q = new QueryExpression("cr07a_equiposdigitalapp")
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacionid",
                    "cr07a_notas",
                    "cr07a_asignadoa"
                )
            };
            q.Criteria.AddCondition("cr07a_ubicacionid", ConditionOperator.Equal, ubicacionId);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid") ? e.FormattedValues["cr07a_ubicacionid"] : "";
                list.Add(new EquipoVm
                {
                    Id = e.Id,
                    Marca = e.GetAttributeValue<string>("cr07a_marca") ?? "",
                    Modelo = e.GetAttributeValue<string>("cr07a_modelo") ?? "",
                    Serial = e.GetAttributeValue<string>("cr07a_serial") ?? "",
                    Estado = estadoFmt,
                    FechaCompra = e.GetAttributeValue<DateTime?>("cr07a_fechacompra"),
                    Ubicacion = e.FormattedValues.ContainsKey("cr07a_ubicacionid") ? e.FormattedValues["cr07a_ubicacionid"] : "",
                    Notas = e.GetAttributeValue<string>("cr07a_notas") ?? "",
                    AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? ""
                });
            }
            return list;
        }

        private async Task<List<EquipoVm>> GetEquiposPorUsuarioUpnAsync(string upn)
        {
            var q = new QueryExpression("cr07a_equiposdigitalapp")
            {
                ColumnSet = new ColumnSet(
                    "cr07a_marca",
                    "cr07a_modelo",
                    "cr07a_serial",
                    "cr07a_estado",
                    "cr07a_fechacompra",
                    "cr07a_ubicacionid",
                    "cr07a_notas",
                    "cr07a_asignadoa"
                )
            };
            q.Criteria.AddCondition("cr07a_asignadoa", ConditionOperator.Equal, upn);

            var result = await _dataverse.RetrieveMultipleAsync(q);
            var list = new List<EquipoVm>(result.Entities.Count);
            foreach (var e in result.Entities)
            {
                var estadoFmt = e.FormattedValues.ContainsKey("cr07a_estado") ? e.FormattedValues["cr07a_estado"] : "";
                var ubicFmt = e.FormattedValues.ContainsKey("cr07a_ubicacionid") ? e.FormattedValues["cr07a_ubicacionid"] : "";
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
                    AsignadoA = e.GetAttributeValue<string>("cr07a_asignadoa") ?? ""
                });
            }
            return list;
        }

        // Conteo de usuarios sin licencia (usa la propiedad assignedLicenses del recurso user)
        private async Task<int> CountUsuariosSinLicenciaAsync()
        {
            HttpClient client;
            try { client = await _graphFactory.CreateClientAsync(); }
            catch { return 0; }

            // Traemos usuarios con assignedLicenses (paginado simple)
            var url = "https://graph.microsoft.com/v1.0/users?$select=id,assignedLicenses&$top=999";
            int count = 0;

            while (!string.IsNullOrEmpty(url))
            {
                var resp = await client.GetAsync(url);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in arr.EnumerateArray())
                    {
                        if (u.TryGetProperty("assignedLicenses", out var lic) && lic.ValueKind == JsonValueKind.Array)
                        {
                            if (lic.GetArrayLength() == 0) count++;
                        }
                    }
                }

                var nextLink = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
                url = nextLink ?? string.Empty;
            }

            return count;
        }

        // Helper para extraer el skiptoken
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
