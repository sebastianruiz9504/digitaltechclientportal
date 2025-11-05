using System;

namespace DigitalTechClientPortal.Models
{
    public class DisponibilidadDto
    {
        public DateTime HoraInicio { get; set; }
        public DateTime HoraFin    { get; set; }
        public bool Disponible     { get; set; }
    }
}