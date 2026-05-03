using System.Globalization;

namespace DigitalTechClientPortal.Models
{
    public sealed class M365GovernanceVm
    {
        public string Period { get; set; } = "D90";
        public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<SecurityDataSourceStatus> DataSources { get; set; } = new();
        public string? GraphError { get; set; }
        public string? AiPlanError { get; set; }
        public M365OptimizationAiPlan? OptimizationPlanAi { get; set; }

        public List<M365LicenseSku> Licenses { get; set; } = new();
        public List<M365UserSnapshot> Users { get; set; } = new();
        public List<M365ActivityUser> ExchangeActivity { get; set; } = new();
        public List<M365ActivityUser> TeamsActivity { get; set; } = new();
        public List<M365StorageWorkloadItem> OneDriveAccounts { get; set; } = new();
        public List<M365StorageWorkloadItem> SharePointSites { get; set; } = new();
        public List<M365PolicySnapshot> IntunePolicies { get; set; } = new();
        public List<M365ManagedDeviceSnapshot> ManagedDevices { get; set; } = new();
        public List<M365EnterpriseAppSnapshot> EnterpriseApps { get; set; } = new();
        public List<M365ServiceHealthItem> ServiceHealth { get; set; } = new();
        public List<M365MessageCenterItem> MessageCenter { get; set; } = new();
        public M365PurviewSummary Purview { get; set; } = new();

        public List<M365WorkloadSummary> Workloads { get; set; } = new();
        public List<M365OptimizationOpportunity> Opportunities { get; set; } = new();
        public List<M365UserOptimizationCandidate> UsersWithoutCoreActivity { get; set; } = new();
        public List<M365StorageWorkloadItem> LargeInactiveOneDriveAccounts { get; set; } = new();
        public List<M365StorageWorkloadItem> LargeInactiveSharePointSites { get; set; } = new();
        public List<M365ManagedDeviceSnapshot> StaleOrNonCompliantDevices { get; set; } = new();
        public List<M365PolicySnapshot> UnassignedPolicies { get; set; } = new();

        public bool CanJoinReportsToUsers { get; set; }
        public string UsageIdentityNote { get; set; } = "";

        public int PeriodDays => Period.ToUpperInvariant() switch
        {
            "D7" => 7,
            "D30" => 30,
            "D180" => 180,
            _ => 90
        };

        public int TotalPurchasedLicenses => Licenses.Sum(l => l.EnabledUnits);
        public int AssignedLicenses => Licenses.Sum(l => l.ConsumedUnits);
        public int AvailableLicenses => Licenses.Sum(l => l.AvailableUnits);
        public int LicensedEnabledUsers => Users.Count(u => u.AccountEnabled && u.AssignedLicenseCount > 0);
        public int GuestUsers => Users.Count(u => string.Equals(u.UserType, "Guest", StringComparison.OrdinalIgnoreCase));
        public int DisabledUsers => Users.Count(u => !u.AccountEnabled);
        public int ActiveServiceIssues => ServiceHealth.Count(h => !h.IsHealthy);
        public int MajorUpcomingChanges => MessageCenter.Count(m => m.IsMajorChange || m.ActionRequiredByDateTime.HasValue);
        public int NonCompliantDevices => ManagedDevices.Count(d => d.IsNonCompliant);
        public int StaleDevices => ManagedDevices.Count(d => d.IsStale);
        public int IntunePolicyCount => IntunePolicies.Count;

        public string GeneratedAtLocal =>
            GeneratedAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
    }

    public sealed class M365LicenseSku
    {
        public string SkuId { get; set; } = "";
        public string SkuPartNumber { get; set; } = "";
        public string CapabilityStatus { get; set; } = "";
        public int EnabledUnits { get; set; }
        public int ConsumedUnits { get; set; }
        public int WarningUnits { get; set; }
        public int SuspendedUnits { get; set; }
        public int LockedOutUnits { get; set; }
        public List<string> ServicePlans { get; set; } = new();
        public int AvailableUnits => Math.Max(EnabledUnits - ConsumedUnits, 0);
        public double UtilizationPercent => EnabledUnits <= 0 ? 0 : Math.Round(ConsumedUnits * 100.0 / EnabledUnits, 1);
        public string DisplayName => string.IsNullOrWhiteSpace(SkuPartNumber) ? SkuId : SkuPartNumber.Replace("_", " ");
    }

    public sealed class M365UserSnapshot
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string Mail { get; set; } = "";
        public string UserType { get; set; } = "";
        public bool AccountEnabled { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public List<string> AssignedLicenseSkuIds { get; set; } = new();
        public int AssignedLicenseCount => AssignedLicenseSkuIds.Count;
    }

    public sealed class M365ActivityUser
    {
        public string Workload { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime? LastActivityDate { get; set; }
        public bool IsLicensed { get; set; }
        public int ReportPeriod { get; set; }
        public int TotalActivityCount { get; set; }
        public Dictionary<string, int> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string AssignedProducts { get; set; } = "";
    }

    public sealed class M365StorageWorkloadItem
    {
        public string Workload { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string Owner { get; set; } = "";
        public string OwnerPrincipalName { get; set; } = "";
        public DateTime? LastActivityDate { get; set; }
        public long StorageUsedBytes { get; set; }
        public long StorageAllocatedBytes { get; set; }
        public long FileCount { get; set; }
        public long ActiveFileCount { get; set; }
        public long PageViewCount { get; set; }
        public long VisitedPageCount { get; set; }
        public string Template { get; set; } = "";
        public int ReportPeriod { get; set; }
        public bool IsInactiveForPeriod { get; set; }
        public double StorageUsedGb => Math.Round(StorageUsedBytes / 1024d / 1024d / 1024d, 2);
        public double StorageAllocatedGb => Math.Round(StorageAllocatedBytes / 1024d / 1024d / 1024d, 2);
    }

    public sealed class M365WorkloadSummary
    {
        public string Name { get; set; } = "";
        public string Area { get; set; } = "";
        public int TotalItems { get; set; }
        public int ActiveItems { get; set; }
        public int InactiveItems { get; set; }
        public long StorageUsedBytes { get; set; }
        public long ActivityCount { get; set; }
        public double ActivityPercent => TotalItems <= 0 ? 0 : Math.Round(ActiveItems * 100.0 / TotalItems, 1);
        public double StorageUsedGb => Math.Round(StorageUsedBytes / 1024d / 1024d / 1024d, 2);
    }

    public sealed class M365PolicySnapshot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string PolicyType { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Technology { get; set; } = "";
        public bool? IsAssigned { get; set; }
        public int? SettingCount { get; set; }
        public DateTimeOffset? CreatedDateTime { get; set; }
        public DateTimeOffset? LastModifiedDateTime { get; set; }
    }

    public sealed class M365ManagedDeviceSnapshot
    {
        public string Id { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string OperatingSystem { get; set; } = "";
        public string ComplianceState { get; set; } = "";
        public string ManagementAgent { get; set; } = "";
        public DateTimeOffset? LastSyncDateTime { get; set; }
        public bool IsNonCompliant => !string.IsNullOrWhiteSpace(ComplianceState)
            && !string.Equals(ComplianceState, "compliant", StringComparison.OrdinalIgnoreCase);
        public bool IsStale => LastSyncDateTime.HasValue && LastSyncDateTime.Value < DateTimeOffset.UtcNow.AddDays(-30);
    }

    public sealed class M365EnterpriseAppSnapshot
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string AppId { get; set; } = "";
        public bool AccountEnabled { get; set; }
        public bool AppRoleAssignmentRequired { get; set; }
        public string SignInAudience { get; set; } = "";
    }

    public sealed class M365ServiceHealthItem
    {
        public string Service { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsHealthy => string.Equals(Status, "serviceOperational", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class M365MessageCenterItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Severity { get; set; } = "";
        public bool IsMajorChange { get; set; }
        public DateTimeOffset? ActionRequiredByDateTime { get; set; }
        public DateTimeOffset? LastModifiedDateTime { get; set; }
        public string Services { get; set; } = "";
    }

    public sealed class M365PurviewSummary
    {
        public int EdiscoveryCases { get; set; }
        public int SensitivityLabels { get; set; }
        public bool EdiscoveryAvailable { get; set; }
        public bool LabelsAvailable { get; set; }
    }

    public sealed class M365OptimizationOpportunity
    {
        public string Area { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Severity { get; set; } = "Media";
        public int Count { get; set; }
        public string RecommendedAction { get; set; } = "";
    }

    public sealed class M365UserOptimizationCandidate
    {
        public string DisplayName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public int LicenseCount { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed class M365OptimizationAiPlan
    {
        public string ExecutiveSummary { get; set; } = "";
        public string OptimizationLevel { get; set; } = "";
        public string OptimizationRationale { get; set; } = "";
        public string GeneratedAtLocal { get; set; } = "";
        public List<M365OptimizationAiFinding> KeyFindings { get; set; } = new();
        public List<M365OptimizationAiAction> Actions { get; set; } = new();
        public List<string> MissingData { get; set; } = new();
        public List<string> Assumptions { get; set; } = new();
    }

    public sealed class M365OptimizationAiFinding
    {
        public string Area { get; set; } = "";
        public string Title { get; set; } = "";
        public string PlainLanguageSummary { get; set; } = "";
        public string AffectedPopulation { get; set; } = "";
        public string WhyItMatters { get; set; } = "";
    }

    public sealed class M365OptimizationAiAction
    {
        public string Priority { get; set; } = "";
        public string Area { get; set; } = "";
        public string Title { get; set; } = "";
        public string BusinessOutcome { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Effort { get; set; } = "";
        public List<M365OptimizationAiStep> Steps { get; set; } = new();
    }

    public sealed class M365OptimizationAiStep
    {
        public int Order { get; set; }
        public string Portal { get; set; } = "";
        public string ClickPath { get; set; } = "";
        public string Instruction { get; set; } = "";
        public string Validation { get; set; } = "";
    }
}
