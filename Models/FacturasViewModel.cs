using System.Collections.Generic;
using DigitalTechClientPortal.Domain.Dataverse;

namespace DigitalTechClientPortal.Web.Models;

public sealed class FacturasViewModel
{
    public IReadOnlyList<FacturaDto> Facturas { get; set; } = new List<FacturaDto>();
}