namespace DigitalTechClientPortal.Web.Models
{
    public sealed class PortfolioCopiersCard
    {
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string[] Bullets { get; set; } = Array.Empty<string>();
        public string? CtaPrimaryText { get; set; } = "Explorar";
        public string? CtaPrimaryHref { get; set; } = "#";
        public string? CtaSecondaryText { get; set; } = "Cotizar";
        public string? CtaSecondaryHref { get; set; } = "#";
        public string IconKind { get; set; } = "printer";
        public string Accent { get; set; } = "red";
    }

    public sealed class PortfolioCopiersViewModel
    {
        public string HeroTitle { get; set; } = "Portafolio Copiers & Printing";
        public string HeroSubtitle { get; set; } = "Soluciones Kyocera para impresión, copiado y gestión documental con eficiencia, calidad y soporte integral.";
        public List<PortfolioCopiersCard> Cards { get; set; } = new();
    }
}