using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public class ImpresorasVm
    {
        public List<ImpresoraVm> Impresoras { get; set; } = new();
        public List<ClienteFiltroVm> Clientes { get; set; } = new();
        public Guid? ClienteSeleccionadoId { get; set; }
        public bool PuedeFiltrarPorCliente { get; set; }
    }

    public class ClienteFiltroVm
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public class ImpresoraVm
    {
        public Guid Id { get; set; }
        public string Serial { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public string Referencia { get; set; } = string.Empty;
        public string UltimoNivelToner { get; set; } = string.Empty;
        public DateTime? FechaUltimaLectura { get; set; }
        public List<MantenimientoVm> Mantenimientos { get; set; } = new();
        public List<ContadorVm> Contadores { get; set; } = new();
    }

    public class MantenimientoVm
    {
        public Guid Id { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public DateTime? FechaMantenimiento { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public bool TieneActa { get; set; }
    }

    public class ContadorVm
    {
        public string Periodo { get; set; } = string.Empty;
        public string ContadorPaginas { get; set; } = string.Empty;
        public DateTime? FechaLectura { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string NivelToner { get; set; } = string.Empty;
        public string Escaneos { get; set; } = string.Empty;
    }
}
