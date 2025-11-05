namespace DigitalTechClientPortal.Web.Models
{
    public sealed class PortfolioCloudCard
    {
        public string Slug { get; set; } = string.Empty;      // para anchors internos
        public string Title { get; set; } = string.Empty;     // ej. Microsoft 365
        public string Subtitle { get; set; } = string.Empty;  // ej. Licenciamiento por usuario
        public string[] Bullets { get; set; } = Array.Empty<string>();
        public string? CtaPrimaryText { get; set; } = "Explorar";
        public string? CtaPrimaryHref { get; set; } = "#";
        public string? CtaSecondaryText { get; set; } = "Cotizar";
        public string? CtaSecondaryHref { get; set; } = "#";
        public string IconKind { get; set; } = "grid";        // selector simple para SVG
        public string Accent { get; set; } = "indigo";        // para bordes/gradientes
    }

    public sealed class PortfolioCloudViewModel
    {
        public string HeroTitle { get; set; } = "Portafolio Cloud";
        public string HeroSubtitle { get; set; } = "Soluciones integrales en productividad, seguridad, continuidad, analítica y IA — implementadas con excelencia.";
        public List<PortfolioCloudCard> Cards { get; set; } = new();
    }
}