namespace DigitalTechClientPortal.Models
{
    public class CapacitacionDto
    {
        public DateTime Fecha { get; set; }
        public decimal DuracionHoras { get; set; }
        public int CantidadAsistentes { get; set; }
        public string Tema { get; set; } = string.Empty;
        public string? CuestionarioUrl { get; set; } // enlace para descargar adjunto
    }
}