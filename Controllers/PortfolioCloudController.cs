using DigitalTechClientPortal.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace DigitalTechClientPortal.Web.Controllers
{
    public sealed class PortafolioCloudController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            var vm = new PortfolioCloudViewModel
            {
                HeroTitle = "Portafolio Cloud",
                HeroSubtitle = "Moderniza tu negocio con soluciones seguras y escalables. Productividad, seguridad, continuidad, analítica e IA en un solo lugar."
            };

            vm.Cards = new List<PortfolioCloudCard>
            {
                new PortfolioCloudCard
                {
                    Slug = "m365",
                    Title = "Licenciamiento Microsoft 365",
                    Subtitle = "Suite completa, por usuario",
                    Bullets = new[]
                    {
                        "Aplicaciones premium (Word, Excel, PowerPoint, Outlook, Teams).",
                        "Seguridad y cumplimiento integrados.",
                        "Escalable: desde PyME a Enterprise."
                    },
                    CtaPrimaryText = "Explorar planes",
                    CtaPrimaryHref = "https://www.microsoft.com/es-ES/Licensing/product-licensing/microsoft-365",
                    CtaSecondaryText = "Cotizar",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "apps",
                    Accent = "indigo"
                },
                new PortfolioCloudCard
                {
                    Slug = "acronis",
                    Title = "Respaldo y Continuidad",
                    Subtitle = "Acronis Cyber Protect",
                    Bullets = new[]
                    {
                        "Backup + ciberseguridad en una sola plataforma.",
                        "Recuperación rápida ante fallos o ransomware.",
                        "Gestión centralizada y anti‑ransomware con IA."
                    },
                    CtaPrimaryText = "Solicitar demo",
                    CtaPrimaryHref = "#demo-acronis",
                    CtaSecondaryText = "Cotizar",
                    CtaSecondaryHref = "#contacto",
                    IconKind = "shield-check",
                    Accent = "cyan"
                },
                new PortfolioCloudCard
                {
                    Slug = "seguridad",
                    Title = "Seguridad Microsoft",
                    Subtitle = "Defender + Sentinel",
                    Bullets = new[]
                    {
                        "Defensa de endpoints con IA (EDR/XDR).",
                        "SIEM nativo en la nube, automatización SOAR.",
                        "Cobertura multinube y multiplataforma."
                    },
                    CtaPrimaryText = "Ver capacidades",
                    CtaPrimaryHref = "https://www.microsoft.com/es-co/security/business/siem-and-xdr/microsoft-sentinel",
                    CtaSecondaryText = "Evaluación de seguridad",
                    CtaSecondaryHref = "#assessment",
                    IconKind = "lock",
                    Accent = "blue"
                },
                new PortfolioCloudCard
                {
                    Slug = "fabric",
                    Title = "Análisis de Datos",
                    Subtitle = "Microsoft Fabric",
                    Bullets = new[]
                    {
                        "Plataforma unificada: datos, BI y ciencia de datos.",
                        "OneLake: un solo lago de datos para toda la org.",
                        "Copilot integrado para acelerar insights."
                    },
                    CtaPrimaryText = "Explorar Fabric",
                    CtaPrimaryHref = "https://www.microsoft.com/es-es/microsoft-fabric",
                    CtaSecondaryText = "Arquitectura de datos",
                    CtaSecondaryHref = "#arquitectura-datos",
                    IconKind = "spark",
                    Accent = "fuchsia"
                },
                new PortfolioCloudCard
                {
                    Slug = "copilot",
                    Title = "AI con Microsoft Copilot",
                    Subtitle = "Productividad y agentes",
                    Bullets = new[]
                    {
                        "Copilot en Word, Excel, PowerPoint, Outlook y Teams.",
                        "IA preparada para empresas: seguridad y cumplimiento.",
                        "Ahorro de tiempo y ROI medible."
                    },
                    CtaPrimaryText = "Conocer Copilot",
                    CtaPrimaryHref = "https://www.microsoft.com/es-es/microsoft-365/copilot/enterprise",
                    CtaSecondaryText = "Habilitar en mi org",
                    CtaSecondaryHref = "#habilitacion-copilot",
                    IconKind = "stars",
                    Accent = "violet"
                }
            };

            return View(vm);
        }
    }
}