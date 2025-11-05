namespace DigitalTechClientPortal.Web.Models
{
    public sealed class ContactCard
    {
        public string RoleLabel { get; set; } = string.Empty; // Soporte Cloud / Soporte Copiers / Account Manager
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; } // opcional
        public string? PhotoUrl { get; set; } // data URL base64 si hay foto, o null
        public string? WhatsAppLink { get; set; } // enlace calculado a wa.me
        public string Initials => GetInitials(FullName);

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "–";
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[^1].Substring(0, 1)).ToUpperInvariant();
        }
    }

    public sealed class RightContactsModel
    {
        public ContactCard? CloudSupport { get; set; }
        public ContactCard? CopiersSupport { get; set; }
        public ContactCard? AccountManager { get; set; }
    }
}