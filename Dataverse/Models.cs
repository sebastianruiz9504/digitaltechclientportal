using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Domain.Dataverse;

public sealed class FacturaDto
{
    public Guid Id { get; init; }
    public string? Numero { get; init; }
    public DateTime? Fecha { get; init; }
    public decimal? Monto { get; init; }
    public string? MontoFormatted { get; init; }
    public int? EstadoValue { get; init; }
    public string? EstadoLabel { get; init; }
    public Guid? ClienteId { get; init; }
    public string? PublicUrl { get; init; }
    public IReadOnlyDictionary<string, string> FormattedValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}