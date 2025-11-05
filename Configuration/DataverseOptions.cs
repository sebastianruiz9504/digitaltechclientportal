// DataverseOptions.cs
namespace DigitalTechClientPortal.Configuration; // ← ajusta al namespace raíz de tu solución

using System.ComponentModel.DataAnnotations;

public sealed class DataverseOptions
{
    [Required] public string Url { get; init; } = default!;
    [Required] public string ClientId { get; init; } = default!;
    [Required] public string ClientSecret { get; init; } = default!;
    [Required] public string TenantId { get; init; } = default!;
}