using DigitalTechClientPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Web.Controllers
{
    public sealed class PortafolioCopiersController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            var vm = new PortfolioCopiersViewModel
            {
                HeroTitle = "Portafolio Copiers & Printing",
                HeroSubtitle = "Equipos multifuncionales, suministros y servicios Kyocera para optimizar tu flujo documental."
            };

            vm.Cards = new List<PortfolioCopiersCard>
            {
                new PortfolioCopiersCard
                {
                    Slug = "centros-copiado",
                    Title = "Centros de Copiado e Impresión",
                    Subtitle = "Soluciones integrales para alto volumen",
                    Bullets = new[]
                    {
                        "Equipos Kyocera TASKalfa y ECOSYS de alto rendimiento.",
                        "Impresión monocromo y color con calidad profesional.",
                        "Optimización de costos y consumo energético."
                    },
                    CtaPrimaryText = "Ver soluciones",
                    CtaPrimaryHref = "#centros-copiado",
                    CtaSecondaryText = "Solicitar asesoría",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "building",
                    Accent = "red"
                },
                new PortfolioCopiersCard
                {
                    Slug = "mantenimiento",
                    Title = "Contratos de Mantenimiento",
                    Subtitle = "Para equipos propios",
                    Bullets = new[]
                    {
                        "Soporte preventivo y correctivo certificado.",
                        "Repuestos y mano de obra incluidos.",
                        "Maximiza la vida útil y disponibilidad de tus equipos."
                    },
                    CtaPrimaryText = "Conocer planes",
                    CtaPrimaryHref = "#mantenimiento",
                    CtaSecondaryText = "Cotizar",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "wrench",
                    Accent = "amber"
                },
                new PortfolioCopiersCard
                {
                    Slug = "suministros",
                    Title = "Venta de Suministros Kyocera",
                    Subtitle = "Tóner, tambores y repuestos originales",
                    Bullets = new[]
                    {
                        "Consumibles 100% originales para máxima calidad.",
                        "Mayor rendimiento y menor costo por página.",
                        "Disponibilidad inmediata y entregas programadas."
                    },
                    CtaPrimaryText = "Ver catálogo",
                    CtaPrimaryHref = "#suministros",
                    CtaSecondaryText = "Solicitar pedido",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "drop",
                    Accent = "emerald"
                },
                new PortfolioCopiersCard
                {
                    Slug = "venta-renta",
                    Title = "Venta y Renta de Impresoras y Multifuncionales",
                    Subtitle = "Flexibilidad para tu negocio",
                    Bullets = new[]
                    {
                        "Venta de equipos nuevos con garantía oficial.",
                        "Planes de renta operativa con mantenimiento incluido.",
                        "Escalabilidad según tus necesidades."
                    },
                    CtaPrimaryText = "Explorar opciones",
                    CtaPrimaryHref = "#venta-renta",
                    CtaSecondaryText = "Cotizar",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "printer",
                    Accent = "blue"
                }
            };

            return View(vm);
        }
    }
}