namespace DigitalTechClientPortal.Models
{
    public class ResumenDto
    {
        public int TicketsCloud { get; set; }
        public int TicketsCopiers { get; set; }
        public int Capacitaciones { get; set; }
        public decimal HorasEntregadas { get; set; }
        public int Reportes { get; set; }
    }

    public enum RangoResumen
    {
        Mes,
        Anio,
        Total
    }
}