using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Web.Models;
using DigitalTechClientPortal.Security;
using DigitalTechClientPortal.Services;

namespace DigitalTechClientPortal.Web.Controllers
{
    [RequireModule(PortalModuleKeys.Licenciamiento)]
    public class LicenciamientoController : Controller
    {
        [HttpGet]
        public Task<IActionResult> Index(string? clienteId = null)
        {
            var email = UserEmailResolver.GetCurrentEmail(User) ?? string.Empty;
            var isAdminLic = email.Equals("sruiz@digitaltechcolombia.com", StringComparison.OrdinalIgnoreCase);

            var precios = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Business Premium"] = 22m,
                ["Business Standard"] = 12.50m,
                ["Business Basic"] = 6m
            };

            var vm = new LicenciamientoViewModel
            {
                ClienteNombre = "Digital Tech",
                FechaCorte = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 15),
                PuedeCambiarCliente = isAdminLic,
                ClienteSeleccionadoId = clienteId,
                ClientesDisponibles = new List<ClienteLookupVm>
                {
                    new() { Id = "digital-tech", Nombre = "Digital Tech" },
                    new() { Id = "cliente-demo", Nombre = "Cliente Demo" }
                },
                ProductosRazonPadre = new List<LicenciaProductoResumenVm>
                {
                    new() { Producto = "Business Premium", CantidadTotal = 300, PrecioUnitarioUsd = precios["Business Premium"] },
                    new() { Producto = "Business Standard", CantidadTotal = 100, PrecioUnitarioUsd = precios["Business Standard"] }
                },
                SubRazones = new List<SubRazonSocialVm>
                {
                    new()
                    {
                        Nombre = "Digital Tech Servicios",
                        Consumo = new List<ConsumoLicenciaVm>
                        {
                            new() { Producto = "Business Premium", Cantidad = 200, DiasConsumo = 30, PrecioUnitarioUsd = precios["Business Premium"] },
                            new() { Producto = "Business Standard", Cantidad = 100, DiasConsumo = 20, PrecioUnitarioUsd = precios["Business Standard"] }
                        }
                    },
                    new()
                    {
                        Nombre = "Digital Tech Envios",
                        Consumo = new List<ConsumoLicenciaVm>
                        {
                            new() { Producto = "Business Premium", Cantidad = 100, DiasConsumo = 30, PrecioUnitarioUsd = precios["Business Premium"] },
                            new() { Producto = "Business Basic", Cantidad = 2, DiasConsumo = 15, PrecioUnitarioUsd = precios["Business Basic"] }
                        }
                    }
                },
                HistoricoSolicitudes = new List<SolicitudLicenciaVm>
                {
                    new() { FechaSolicitud = DateTime.UtcNow.AddDays(-4), SolicitadoPor = "cliente@digitaltech.com", SubRazon = "Digital Tech Servicios", Producto = "Business Standard", CantidadNueva = 10, Estado = "Aprobada" },
                    new() { FechaSolicitud = DateTime.UtcNow.AddDays(-1), SolicitadoPor = email, SubRazon = "Razon Padre", Producto = "Business Premium", CantidadNueva = 25, Estado = "Pendiente" }
                }
            };

            return Task.FromResult<IActionResult>(View(vm));
        }
    }
}
