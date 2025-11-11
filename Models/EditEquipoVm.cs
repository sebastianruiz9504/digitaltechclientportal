using System;
using System.ComponentModel.DataAnnotations;

namespace DigitalTechClientPortal.Models
{
    public sealed class EditEquipoVm
    {
        [Required]
        public Guid Id { get; set; }

        [Required]
        public Guid CategoriaId { get; set; }

        [Required]
        [StringLength(100)]
        public string Marca { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Modelo { get; set; } = string.Empty;

        [StringLength(150)]
        public string Serial { get; set; } = string.Empty;

        public int? Estado { get; set; } // 645250000, 645250001, 645250002

        [DataType(DataType.Date)]
        public DateTime? FechaCompra { get; set; }

        [StringLength(1000)]
        public string Notas { get; set; } = string.Empty;

        [StringLength(200)]
        public string AsignadoA { get; set; } = string.Empty;

        public Guid? UbicacionId { get; set; }
    }
}