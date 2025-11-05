using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public interface ICapacitacionService
    {
        List<CapacitacionDto> ObtenerCapacitaciones();
        Task<List<CapacitacionDto>> GetCapacitacionesAsync();
        byte[]? DescargarCuestionario(Guid id);
    }
}