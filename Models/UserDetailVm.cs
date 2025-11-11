using System.Collections.Generic;

namespace DigitalTechClientPortal.Models
{
    public class LicenseAssignmentViewModel
    {
        public string SkuId { get; set; } = string.Empty;
        public string SkuPartNumber { get; set; } = string.Empty;
        public string CapabilityStatus { get; set; } = string.Empty;
        public List<string> ServicePlans { get; set; } = new();
    }

    public class UserDetailVm
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string UserPrincipalName { get; set; } = string.Empty;
        public string Mail { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string MobilePhone { get; set; } = string.Empty;
        public string BusinessPhones { get; set; } = string.Empty;
        public string OfficeLocation { get; set; } = string.Empty;

        public string? PhotoBase64 { get; set; }

        public List<LicenseAssignmentViewModel> Licenses { get; set; } = new();
        public List<EquipoVm> EquiposAsignados { get; set; } = new();
    }
        public class PagedUsersViewModel
    {
        public List<UserInventoryViewModel> Users { get; set; } = new();
        public string? NextSkipToken { get; set; }
        public int PageSize { get; set; }
        public string? Term { get; set; } // término de búsqueda global (opcional)
        public bool IsSearch => !string.IsNullOrWhiteSpace(Term);
    }
}

