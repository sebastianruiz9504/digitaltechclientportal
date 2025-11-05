using System.Collections.Generic;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Models.ViewModels
{
    public sealed class CalendarioViewModel
    {
        public List<CapacitacionDto> Capacitaciones { get; set; } = new();
        public List<DisponibilidadDto> Disponibilidad { get; set; } = new();
    }
}