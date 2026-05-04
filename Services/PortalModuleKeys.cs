using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalTechClientPortal.Services
{
    public static class PortalModuleKeys
    {
        public const string Facturacion = "facturacion";
        public const string Capacitaciones = "capacitaciones";
        public const string Academy = "academy";
        public const string Reportes = "reportes";
        public const string Soporte = "soporte";
        public const string Seguridad = "seguridad";
        public const string Gobierno = "gobierno";
        public const string Inventario = "inventario";
        public const string Impresoras = "impresoras";

        public static readonly IReadOnlyList<PortalModuleDefinition> All = new List<PortalModuleDefinition>
        {
            new(Facturacion, "Facturacion", "Facturacion", "Index", "ti-file-invoice"),
            new(Capacitaciones, "Capacitaciones", "Capacitaciones", "Index", "ti-school"),
            new(Academy, "Academy", "Academy", "Index", "ti-book"),
            new(Reportes, "Reportes", "Reportes", "Index", "ti-report"),
            new(Soporte, "Soporte", "Soporte", "Index", "ti-headset"),
            new(Seguridad, "Seguridad", "Seguridad", "Panel", "ti-shield-lock"),
            new(Gobierno, "Gobierno M365", "Gobierno", "Index", "ti-chart-pie"),
            new(Inventario, "Inventario", "Inventario", "Equipos", "ti-device-desktop"),
            new(Impresoras, "Impresoras", "Impresoras", "Index", "ti-printer")
        };

        public static readonly IReadOnlySet<string> AllKeys = All
            .Select(m => m.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public static bool IsValid(string moduleKey)
        {
            return !string.IsNullOrWhiteSpace(moduleKey) && AllKeys.Contains(moduleKey);
        }
    }

    public sealed record PortalModuleDefinition(
        string Key,
        string Label,
        string Controller,
        string Action,
        string Icon);
}
