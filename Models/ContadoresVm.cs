using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public class ContadoresIndexVm
    {
        public List<ClienteFiltroVm> Clientes { get; set; } = new();
        public List<ConsumoEquipoVm> Equipos { get; set; } = new();
        public List<ConsumoClienteVm> ConsumoPorCliente { get; set; } = new();

        public Guid? ClienteSeleccionadoId { get; set; }
        public int MesSeleccionado { get; set; }
        public int AnioSeleccionado { get; set; }

        public DateTime PeriodoSeleccionado => new(AnioSeleccionado, MesSeleccionado, 1);
        public string Mensaje { get; set; } = string.Empty;
    }

    public class ConsumoEquipoVm
    {
        public Guid EquipoId { get; set; }
        public string EquipoNombre { get; set; } = string.Empty;
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;

        public DateTime? FechaActual { get; set; }
        public DateTime? FechaAnterior { get; set; }

        public long? ContadorActualCopias { get; set; }
        public long? ConsumoCopias { get; set; }

        public long? ContadorActualEscaneos { get; set; }
        public long? ConsumoEscaneos { get; set; }

        public int? DiasEntreTomas { get; set; }

        public long ConsumoTotal => (ConsumoCopias ?? 0) + (ConsumoEscaneos ?? 0);
    }

    public class ConsumoClienteVm
    {
        public Guid ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public long TotalCopias { get; set; }
        public long TotalEscaneos { get; set; }
        public long TotalConsumo => TotalCopias + TotalEscaneos;
        public int EquiposConConsumo { get; set; }
    }
}
