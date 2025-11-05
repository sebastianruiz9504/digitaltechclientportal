using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using DigitalTechClientPortal.Web.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalTechClientPortal.Web.Services
{
    public sealed class ContactsPanelService
    {
        private readonly ServiceClient _svc;
        private readonly IHttpContextAccessor _http;

        // Columnas necesarias de systemuser (incluye tu columna de imagen personalizada)
        private static readonly ColumnSet UserCols = new ColumnSet(
            "fullname",
            "internalemailaddress",
            "address1_telephone1",
            "cr07a_foto",   // imagen personalizada como byte[]
            "entityimage"   // imagen estándar como byte[] (fallback)
        );

        public ContactsPanelService(ServiceClient svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        public async Task<RightContactsModel?> GetRightContactsAsync(Guid? clienteId = null)
        {
            // 1) Resolver cliente
            var cliente = await ResolveClienteAsync(clienteId);
            if (cliente == null) return null;

            // 2) Lookups hacia systemuser
            var cloudRef   = cliente.GetAttributeValue<EntityReference>("cr07a_cloudsupport");
            var copiersRef = cliente.GetAttributeValue<EntityReference>("cr07a_copierssupport");
            var ownerRef   = cliente.GetAttributeValue<EntityReference>("ownerid");

            // 3) Armar modelo final
            var model = new RightContactsModel
            {
                CloudSupport   = await BuildContactCardAsync(cloudRef,   "Soporte Cloud"),
                CopiersSupport = await BuildContactCardAsync(copiersRef, "Soporte Copiers"),
                AccountManager = await BuildContactCardAsync(ownerRef,   "Account Manager")
            };

            return model;
        }

        private async Task<Entity?> ResolveClienteAsync(Guid? clienteId)
        {
            if (clienteId.HasValue && clienteId.Value != Guid.Empty)
            {
                return await _svc.RetrieveAsync(
                    "cr07a_cliente",
                    clienteId.Value,
                    new ColumnSet("cr07a_cloudsupport", "cr07a_copierssupport", "ownerid")
                );
            }

            // Fallback: correlación por correo del usuario autenticado
            var email = GetLoginEmail();
            if (string.IsNullOrWhiteSpace(email)) return null;

            var q = new QueryExpression("cr07a_cliente")
            {
                ColumnSet = new ColumnSet("cr07a_cloudsupport", "cr07a_copierssupport", "ownerid"),
                Criteria = new FilterExpression(LogicalOperator.And)
            };
            q.Criteria.AddCondition("cr07a_correoelectronico", ConditionOperator.Equal, email);

            var res = await _svc.RetrieveMultipleAsync(q);
            return res.Entities.FirstOrDefault();
        }

        private async Task<ContactCard?> BuildContactCardAsync(EntityReference? userRef, string role)
        {
            if (userRef == null || userRef.Id == Guid.Empty) return null;

            var user = await _svc.RetrieveAsync(userRef.LogicalName, userRef.Id, UserCols);
            if (user == null) return null;

            var fullName = user.GetAttributeValue<string>("fullname") ?? string.Empty;
            var email    = user.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;
            var phone    = user.GetAttributeValue<string>("address1_telephone1");

            // Foto: intenta primero con cr07a_foto; si no, usar entityimage
            string? photoUrl = TryBuildDataUriFromBytes(user, "cr07a_foto")
                               ?? TryBuildDataUriFromBytes(user, "entityimage");

            return new ContactCard
            {
                RoleLabel    = role,
                FullName     = fullName,
                Email        = email,
                Phone        = phone,
                PhotoUrl     = photoUrl, // data:image/png;base64,... o null
                WhatsAppLink = BuildWhatsAppLink(phone, $"Hola {fullName}, me gustaría contactarte.")
            };
        }

        // Convierte byte[] en Data URI base64; si no existe o está vacío, devuelve null
        private static string? TryBuildDataUriFromBytes(Entity entity, string attributeName)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var val) || val is not byte[] bytes) return null;
            if (bytes.Length == 0) return null;

            // Asume PNG; si sabes que usas JPG, cambia el MIME a image/jpeg
            var base64 = Convert.ToBase64String(bytes);
            return $"data:image/png;base64,{base64}";
        }

        private string? GetLoginEmail()
        {
            var user = _http.HttpContext?.User;
            if (user == null) return null;

            return user.FindFirst("preferred_username")?.Value
                ?? user.FindFirst("emails")?.Value
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        }

        private static string? BuildWhatsAppLink(string? rawPhone, string message)
        {
            if (string.IsNullOrWhiteSpace(rawPhone)) return null;

            var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits)) return null;

            // Normalización básica para Colombia (+57)
            if (digits.StartsWith("57"))
            {
                // OK
            }
            else if (digits.Length == 10 && digits.StartsWith("3"))
            {
                digits = "57" + digits;
            }
            else if (digits.StartsWith("0"))
            {
                digits = digits.TrimStart('0');
                if (digits.Length == 10) digits = "57" + digits;
            }

            var text = Uri.EscapeDataString(message ?? "Hola, me gustaría contactarte.");
            return $"https://wa.me/{digits}?text={text}";
        }
    }
}