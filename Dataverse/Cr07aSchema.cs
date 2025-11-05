// Domain/Dataverse/Cr07aSchema.cs
namespace DigitalTechClientPortal.Domain.Dataverse;

public static class Cr07aSchema
{
    // Entidades
    public const string ClienteEntity = "cr07a_cliente";
    public const string FacturacionEntity = "cr07a_facturacion";

    // Claves primarias (ajusta si tu solución usa otros nombres)
    public const string ClienteId = "cr07a_nombre";
    public const string FacturacionId = "cr07a_name";

    // Cliente: columnas
    public const string Cliente_Nombre = "cr07a_nombre";   // string
    public const string Cliente_Correo = "cr07a_correoelectronico";   // string (email)

    // Facturación: columnas
    public const string Facturacion_Numero = "cr07a_name"; // string o autonum (opcional)
    public const string Facturacion_Fecha = "cr07a_fechadeemision";   // DateTime
    public const string Facturacion_Monto = "cr07a_totalfactura";          // Money
    public const string Facturacion_Estado = "cr07a_vertical";        // OptionSetValue
    public const string Facturacion_ClienteLookup = "cr07a_clientenit"; // Lookup -> Cliente
    // Facturación: columnas
public const string Facturacion_PublicUrl = "cr07a_publicurl"; // string (URL)

    // Para ordenamiento/presentación
    public const string Facturacion_DefaultOrderDesc = Facturacion_Fecha;
}