using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using DigitalTechClientPortal.Models;

namespace DigitalTechClientPortal.Services
{
    public sealed class M365GovernanceDataService
    {
        private const string GraphV1 = "https://graph.microsoft.com/v1.0";
        private const string GraphBeta = "https://graph.microsoft.com/beta";
        private readonly GraphClientFactory _factory;
        private readonly ILogger<M365GovernanceDataService> _logger;

        public M365GovernanceDataService(GraphClientFactory factory, ILogger<M365GovernanceDataService> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<M365GovernanceVm> CollectAsync(string period = "D90", int top = 200, CancellationToken cancellationToken = default)
        {
            var vm = new M365GovernanceVm
            {
                Period = NormalizePeriod(period),
                GeneratedAtUtc = DateTimeOffset.UtcNow
            };

            try
            {
                using var client = await _factory.CreateClientAsync();

                await FetchLicensesAsync(client, vm, cancellationToken);
                await FetchUsersAsync(client, vm, Math.Max(top, 300), cancellationToken);
                await FetchEmailActivityAsync(client, vm, cancellationToken);
                await FetchTeamsActivityAsync(client, vm, cancellationToken);
                await FetchOneDriveUsageAsync(client, vm, cancellationToken);
                await FetchSharePointUsageAsync(client, vm, cancellationToken);
                await FetchIntunePoliciesAsync(client, vm, cancellationToken);
                await FetchManagedDevicesAsync(client, vm, top, cancellationToken);
                await FetchEnterpriseAppsAsync(client, vm, top, cancellationToken);
                await FetchServiceHealthAsync(client, vm, cancellationToken);
                await FetchMessageCenterAsync(client, vm, cancellationToken);
                await FetchPurviewSignalsAsync(client, vm, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible inicializar Microsoft Graph para Gobierno M365.");
                vm.GraphError = "No fue posible conectar con Microsoft Graph. Inicia sesion nuevamente o valida el consentimiento de permisos del tenant.";
                RegisterDataSource(vm, "Microsoft Graph", false, 0, vm.GraphError);
            }

            BuildDerivedInsights(vm);
            return vm;
        }

        private async Task FetchLicensesAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Licencias";
            var url = $"{GraphV1}/subscribedSkus?$select=skuId,skuPartNumber,capabilityStatus,consumedUnits,prepaidUnits,servicePlans";
            using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken);
            if (doc == null) return;

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var prepaid = item.TryGetProperty("prepaidUnits", out var p) ? p : default;
                    var sku = new M365LicenseSku
                    {
                        SkuId = item.GetStringOrDefault("skuId"),
                        SkuPartNumber = item.GetStringOrDefault("skuPartNumber"),
                        CapabilityStatus = item.GetStringOrDefault("capabilityStatus"),
                        ConsumedUnits = item.GetIntOrDefault("consumedUnits"),
                        EnabledUnits = prepaid.ValueKind == JsonValueKind.Object ? prepaid.GetIntOrDefault("enabled") : 0,
                        WarningUnits = prepaid.ValueKind == JsonValueKind.Object ? prepaid.GetIntOrDefault("warning") : 0,
                        SuspendedUnits = prepaid.ValueKind == JsonValueKind.Object ? prepaid.GetIntOrDefault("suspended") : 0,
                        LockedOutUnits = prepaid.ValueKind == JsonValueKind.Object ? prepaid.GetIntOrDefault("lockedOut") : 0
                    };

                    if (item.TryGetProperty("servicePlans", out var plans) && plans.ValueKind == JsonValueKind.Array)
                    {
                        sku.ServicePlans = plans.EnumerateArray()
                            .Select(p => p.GetStringOrDefault("servicePlanName"))
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(30)
                            .ToList();
                    }

                    vm.Licenses.Add(sku);
                }
            }

            RegisterDataSource(vm, source, true, vm.Licenses.Count, null);
        }

        private async Task FetchUsersAsync(HttpClient client, M365GovernanceVm vm, int top, CancellationToken cancellationToken)
        {
            const string source = "Usuarios";
            var count = 0;
            var url = $"{GraphV1}/users?$select=id,displayName,userPrincipalName,mail,userType,accountEnabled,createdDateTime,assignedLicenses&$top=999";

            while (!string.IsNullOrWhiteSpace(url) && count < top)
            {
                using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken, registerOnFailure: count == 0);
                if (doc == null) break;

                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (count >= top) break;

                        var user = new M365UserSnapshot
                        {
                            Id = item.GetStringOrDefault("id"),
                            DisplayName = item.GetStringOrDefault("displayName"),
                            UserPrincipalName = item.GetStringOrDefault("userPrincipalName"),
                            Mail = item.GetStringOrDefault("mail"),
                            UserType = item.GetStringOrDefault("userType"),
                            AccountEnabled = item.GetBoolOrDefault("accountEnabled"),
                            CreatedDateTime = item.GetDateTimeOffsetOrNull("createdDateTime")
                        };

                        if (item.TryGetProperty("assignedLicenses", out var licenses) && licenses.ValueKind == JsonValueKind.Array)
                        {
                            user.AssignedLicenseSkuIds = licenses.EnumerateArray()
                                .Select(l => l.GetStringOrDefault("skuId"))
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                        }

                        vm.Users.Add(user);
                        count++;
                    }
                }

                url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            }

            if (vm.DataSources.All(s => !string.Equals(s.Name, source, StringComparison.OrdinalIgnoreCase)))
            {
                RegisterDataSource(vm, source, true, vm.Users.Count, null);
            }
        }

        private async Task FetchEmailActivityAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Actividad Exchange";
            var rows = await FetchCsvReportAsync(client, $"{GraphV1}/reports/getEmailActivityUserDetail(period='{vm.Period}')", vm, source, cancellationToken);
            foreach (var row in rows)
            {
                var metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["send"] = GetInt(row, "Send Count"),
                    ["receive"] = GetInt(row, "Receive Count"),
                    ["read"] = GetInt(row, "Read Count"),
                    ["meetingsCreated"] = GetInt(row, "Meeting Created Count"),
                    ["meetingsInteracted"] = GetInt(row, "Meeting Interacted Count")
                };

                vm.ExchangeActivity.Add(new M365ActivityUser
                {
                    Workload = "Exchange",
                    UserPrincipalName = GetString(row, "User Principal Name"),
                    DisplayName = GetString(row, "Display Name"),
                    LastActivityDate = GetDate(row, "Last Activity Date"),
                    ReportPeriod = GetInt(row, "Report Period"),
                    AssignedProducts = GetString(row, "Assigned Products"),
                    TotalActivityCount = metrics.Values.Sum(),
                    Metrics = metrics
                });
            }

            if (!SourceFailed(vm, source))
            {
                RegisterDataSource(vm, source, true, vm.ExchangeActivity.Count, null);
            }
        }

        private async Task FetchTeamsActivityAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Actividad Teams";
            var rows = await FetchCsvReportAsync(client, $"{GraphV1}/reports/getTeamsUserActivityUserDetail(period='{vm.Period}')", vm, source, cancellationToken);
            foreach (var row in rows)
            {
                var metrics = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["teamChat"] = GetInt(row, "Team Chat Message Count"),
                    ["privateChat"] = GetInt(row, "Private Chat Message Count"),
                    ["calls"] = GetInt(row, "Call Count"),
                    ["meetings"] = GetInt(row, "Meeting Count"),
                    ["postMessages"] = GetInt(row, "Post Messages"),
                    ["replyMessages"] = GetInt(row, "Reply Messages"),
                    ["meetingsOrganized"] = GetInt(row, "Meetings Organized Count"),
                    ["meetingsAttended"] = GetInt(row, "Meetings Attended Count")
                };

                vm.TeamsActivity.Add(new M365ActivityUser
                {
                    Workload = "Teams",
                    UserPrincipalName = GetString(row, "User Principal Name"),
                    LastActivityDate = GetDate(row, "Last Activity Date"),
                    IsLicensed = GetBool(row, "Is Licensed"),
                    ReportPeriod = GetInt(row, "Report Period"),
                    AssignedProducts = GetString(row, "Assigned Products"),
                    TotalActivityCount = metrics.Values.Sum(),
                    Metrics = metrics
                });
            }

            if (!SourceFailed(vm, source))
            {
                RegisterDataSource(vm, source, true, vm.TeamsActivity.Count, null);
            }
        }

        private async Task FetchOneDriveUsageAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Uso OneDrive";
            var rows = await FetchCsvReportAsync(client, $"{GraphV1}/reports/getOneDriveUsageAccountDetail(period='{vm.Period}')", vm, source, cancellationToken);
            foreach (var row in rows)
            {
                vm.OneDriveAccounts.Add(new M365StorageWorkloadItem
                {
                    Workload = "OneDrive",
                    Name = GetString(row, "Owner Display Name"),
                    Url = GetString(row, "Site URL"),
                    Owner = GetString(row, "Owner Display Name"),
                    OwnerPrincipalName = GetString(row, "Owner Principal Name"),
                    LastActivityDate = GetDate(row, "Last Activity Date"),
                    FileCount = GetLong(row, "File Count"),
                    ActiveFileCount = GetLong(row, "Active File Count"),
                    StorageUsedBytes = GetLong(row, "Storage Used (Byte)"),
                    StorageAllocatedBytes = GetLong(row, "Storage Allocated (Byte)"),
                    ReportPeriod = GetInt(row, "Report Period")
                });
            }

            if (!SourceFailed(vm, source))
            {
                RegisterDataSource(vm, source, true, vm.OneDriveAccounts.Count, null);
            }
        }

        private async Task FetchSharePointUsageAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Uso SharePoint";
            var rows = await FetchCsvReportAsync(client, $"{GraphV1}/reports/getSharePointSiteUsageDetail(period='{vm.Period}')", vm, source, cancellationToken);
            foreach (var row in rows)
            {
                vm.SharePointSites.Add(new M365StorageWorkloadItem
                {
                    Workload = "SharePoint",
                    Name = GetString(row, "Site URL"),
                    Url = GetString(row, "Site URL"),
                    Owner = GetString(row, "Owner Display Name"),
                    OwnerPrincipalName = GetString(row, "Owner Principal Name"),
                    LastActivityDate = GetDate(row, "Last Activity Date"),
                    FileCount = GetLong(row, "File Count"),
                    ActiveFileCount = GetLong(row, "Active File Count"),
                    PageViewCount = GetLong(row, "Page View Count"),
                    VisitedPageCount = GetLong(row, "Visited Page Count"),
                    StorageUsedBytes = GetLong(row, "Storage Used (Byte)"),
                    StorageAllocatedBytes = GetLong(row, "Storage Allocated (Byte)"),
                    Template = GetString(row, "Root Web Template"),
                    ReportPeriod = GetInt(row, "Report Period")
                });
            }

            if (!SourceFailed(vm, source))
            {
                RegisterDataSource(vm, source, true, vm.SharePointSites.Count, null);
            }
        }

        private async Task FetchIntunePoliciesAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            await FetchPolicyCollectionAsync(
                client,
                vm,
                "Políticas Intune - configuración",
                $"{GraphV1}/deviceManagement/deviceConfigurations?$select=id,displayName,createdDateTime,lastModifiedDateTime&$top=200",
                "Configuración",
                item => item.GetStringOrDefault("displayName"),
                cancellationToken);

            await FetchPolicyCollectionAsync(
                client,
                vm,
                "Políticas Intune - cumplimiento",
                $"{GraphV1}/deviceManagement/deviceCompliancePolicies?$select=id,displayName,createdDateTime,lastModifiedDateTime&$top=200",
                "Cumplimiento",
                item => item.GetStringOrDefault("displayName"),
                cancellationToken);

            await FetchPolicyCollectionAsync(
                client,
                vm,
                "Políticas Intune - catálogo",
                $"{GraphBeta}/deviceManagement/configurationPolicies?$select=id,name,platforms,technologies,isAssigned,settingCount,createdDateTime,lastModifiedDateTime&$top=200",
                "Catálogo de configuración",
                item => item.GetStringOrDefault("name"),
                cancellationToken);
        }

        private async Task FetchPolicyCollectionAsync(
            HttpClient client,
            M365GovernanceVm vm,
            string source,
            string initialUrl,
            string policyType,
            Func<JsonElement, string> getName,
            CancellationToken cancellationToken)
        {
            var count = 0;
            var url = initialUrl;

            while (!string.IsNullOrWhiteSpace(url))
            {
                using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken, registerOnFailure: count == 0);
                if (doc == null) break;

                if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        vm.IntunePolicies.Add(new M365PolicySnapshot
                        {
                            Id = item.GetStringOrDefault("id"),
                            Name = getName(item),
                            PolicyType = policyType,
                            Platform = item.GetStringOrDefault("platforms"),
                            Technology = item.GetStringOrDefault("technologies"),
                            IsAssigned = item.TryGetProperty("isAssigned", out var assigned) && assigned.ValueKind is JsonValueKind.True or JsonValueKind.False
                                ? assigned.GetBoolean()
                                : null,
                            SettingCount = item.TryGetProperty("settingCount", out var settingCount) && settingCount.ValueKind == JsonValueKind.Number && settingCount.TryGetInt32(out var sc)
                                ? sc
                                : null,
                            CreatedDateTime = item.GetDateTimeOffsetOrNull("createdDateTime"),
                            LastModifiedDateTime = item.GetDateTimeOffsetOrNull("lastModifiedDateTime")
                        });
                        count++;
                    }
                }

                url = doc.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            }

            if (vm.DataSources.All(s => !string.Equals(s.Name, source, StringComparison.OrdinalIgnoreCase)))
            {
                RegisterDataSource(vm, source, true, count, null);
            }
        }

        private async Task FetchManagedDevicesAsync(HttpClient client, M365GovernanceVm vm, int top, CancellationToken cancellationToken)
        {
            const string source = "Dispositivos Intune";
            var url = $"{GraphV1}/deviceManagement/managedDevices?$select=id,deviceName,userPrincipalName,operatingSystem,complianceState,managementAgent,lastSyncDateTime&$top={Math.Min(top, 200)}";
            using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken);
            if (doc == null) return;

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    vm.ManagedDevices.Add(new M365ManagedDeviceSnapshot
                    {
                        Id = item.GetStringOrDefault("id"),
                        DeviceName = item.GetStringOrDefault("deviceName"),
                        UserPrincipalName = item.GetStringOrDefault("userPrincipalName"),
                        OperatingSystem = item.GetStringOrDefault("operatingSystem"),
                        ComplianceState = item.GetStringOrDefault("complianceState"),
                        ManagementAgent = item.GetStringOrDefault("managementAgent"),
                        LastSyncDateTime = item.GetDateTimeOffsetOrNull("lastSyncDateTime")
                    });
                }
            }

            RegisterDataSource(vm, source, true, vm.ManagedDevices.Count, null);
        }

        private async Task FetchEnterpriseAppsAsync(HttpClient client, M365GovernanceVm vm, int top, CancellationToken cancellationToken)
        {
            const string source = "Aplicaciones empresariales";
            var url = $"{GraphV1}/servicePrincipals?$select=id,displayName,appId,accountEnabled,appRoleAssignmentRequired,signInAudience&$top={Math.Min(top, 200)}";
            using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken);
            if (doc == null) return;

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    vm.EnterpriseApps.Add(new M365EnterpriseAppSnapshot
                    {
                        Id = item.GetStringOrDefault("id"),
                        DisplayName = item.GetStringOrDefault("displayName"),
                        AppId = item.GetStringOrDefault("appId"),
                        AccountEnabled = item.GetBoolOrDefault("accountEnabled", defaultValue: true),
                        AppRoleAssignmentRequired = item.GetBoolOrDefault("appRoleAssignmentRequired"),
                        SignInAudience = item.GetStringOrDefault("signInAudience")
                    });
                }
            }

            RegisterDataSource(vm, source, true, vm.EnterpriseApps.Count, null);
        }

        private async Task FetchServiceHealthAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Salud Microsoft 365";
            var url = $"{GraphV1}/admin/serviceAnnouncement/healthOverviews";
            using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken);
            if (doc == null) return;

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    vm.ServiceHealth.Add(new M365ServiceHealthItem
                    {
                        Service = item.GetStringOrDefault("service"),
                        Status = item.GetStringOrDefault("status")
                    });
                }
            }

            RegisterDataSource(vm, source, true, vm.ServiceHealth.Count, null);
        }

        private async Task FetchMessageCenterAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string source = "Message Center";
            var url = $"{GraphV1}/admin/serviceAnnouncement/messages?$top=30&$select=id,title,category,severity,isMajorChange,actionRequiredByDateTime,lastModifiedDateTime,services";
            using var doc = await TryGetJsonAsync(client, url, vm, source, cancellationToken);
            if (doc == null) return;

            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    vm.MessageCenter.Add(new M365MessageCenterItem
                    {
                        Id = item.GetStringOrDefault("id"),
                        Title = item.GetStringOrDefault("title"),
                        Category = item.GetStringOrDefault("category"),
                        Severity = item.GetStringOrDefault("severity"),
                        IsMajorChange = item.GetBoolOrDefault("isMajorChange"),
                        ActionRequiredByDateTime = item.GetDateTimeOffsetOrNull("actionRequiredByDateTime"),
                        LastModifiedDateTime = item.GetDateTimeOffsetOrNull("lastModifiedDateTime"),
                        Services = item.GetArrayAsString("services")
                    });
                }
            }

            RegisterDataSource(vm, source, true, vm.MessageCenter.Count, null);
        }

        private async Task FetchPurviewSignalsAsync(HttpClient client, M365GovernanceVm vm, CancellationToken cancellationToken)
        {
            const string casesSource = "Purview eDiscovery";
            using (var cases = await TryGetJsonAsync(client, $"{GraphBeta}/security/cases/ediscoveryCases?$top=50", vm, casesSource, cancellationToken))
            {
                if (cases != null && cases.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    vm.Purview.EdiscoveryCases = arr.GetArrayLength();
                    vm.Purview.EdiscoveryAvailable = true;
                    RegisterDataSource(vm, casesSource, true, vm.Purview.EdiscoveryCases, null);
                }
            }

            const string labelsSource = "Purview etiquetas";
            using (var labels = await TryGetJsonAsync(client, $"{GraphBeta}/informationProtection/policy/labels?$top=50", vm, labelsSource, cancellationToken))
            {
                if (labels != null && labels.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    vm.Purview.SensitivityLabels = arr.GetArrayLength();
                    vm.Purview.LabelsAvailable = true;
                    RegisterDataSource(vm, labelsSource, true, vm.Purview.SensitivityLabels, null);
                }
            }
        }

        private void BuildDerivedInsights(M365GovernanceVm vm)
        {
            var threshold = DateTime.UtcNow.Date.AddDays(-vm.PeriodDays);

            static bool IsActive(DateTime? value, DateTime thresholdDate)
            {
                return value.HasValue && value.Value.Date >= thresholdDate;
            }

            foreach (var item in vm.OneDriveAccounts)
            {
                item.IsInactiveForPeriod = !IsActive(item.LastActivityDate, threshold);
            }

            foreach (var item in vm.SharePointSites)
            {
                item.IsInactiveForPeriod = !IsActive(item.LastActivityDate, threshold);
            }

            var exchangeActive = vm.ExchangeActivity.Count(u => IsActive(u.LastActivityDate, threshold) && u.TotalActivityCount > 0);
            var teamsActive = vm.TeamsActivity.Count(u => IsActive(u.LastActivityDate, threshold) && u.TotalActivityCount > 0);
            var oneDriveActive = vm.OneDriveAccounts.Count(o => !o.IsInactiveForPeriod && o.ActiveFileCount > 0);
            var sharePointActive = vm.SharePointSites.Count(s => !s.IsInactiveForPeriod && (s.ActiveFileCount > 0 || s.PageViewCount > 0 || s.VisitedPageCount > 0));

            vm.Workloads.Add(new M365WorkloadSummary
            {
                Name = "Exchange",
                Area = "Comunicación",
                TotalItems = vm.ExchangeActivity.Count,
                ActiveItems = exchangeActive,
                InactiveItems = Math.Max(vm.ExchangeActivity.Count - exchangeActive, 0),
                ActivityCount = vm.ExchangeActivity.Sum(u => (long)u.TotalActivityCount)
            });

            vm.Workloads.Add(new M365WorkloadSummary
            {
                Name = "Teams",
                Area = "Colaboración",
                TotalItems = vm.TeamsActivity.Count,
                ActiveItems = teamsActive,
                InactiveItems = Math.Max(vm.TeamsActivity.Count - teamsActive, 0),
                ActivityCount = vm.TeamsActivity.Sum(u => (long)u.TotalActivityCount)
            });

            vm.Workloads.Add(new M365WorkloadSummary
            {
                Name = "OneDrive",
                Area = "Archivos personales",
                TotalItems = vm.OneDriveAccounts.Count,
                ActiveItems = oneDriveActive,
                InactiveItems = Math.Max(vm.OneDriveAccounts.Count - oneDriveActive, 0),
                StorageUsedBytes = vm.OneDriveAccounts.Sum(o => o.StorageUsedBytes),
                ActivityCount = vm.OneDriveAccounts.Sum(o => o.ActiveFileCount)
            });

            vm.Workloads.Add(new M365WorkloadSummary
            {
                Name = "SharePoint",
                Area = "Sitios y documentos",
                TotalItems = vm.SharePointSites.Count,
                ActiveItems = sharePointActive,
                InactiveItems = Math.Max(vm.SharePointSites.Count - sharePointActive, 0),
                StorageUsedBytes = vm.SharePointSites.Sum(s => s.StorageUsedBytes),
                ActivityCount = vm.SharePointSites.Sum(s => s.ActiveFileCount + s.PageViewCount + s.VisitedPageCount)
            });

            BuildJoinableUserInsights(vm, threshold);

            vm.LargeInactiveOneDriveAccounts = vm.OneDriveAccounts
                .Where(o => o.IsInactiveForPeriod && o.StorageUsedGb >= 1)
                .OrderByDescending(o => o.StorageUsedBytes)
                .Take(20)
                .ToList();

            vm.LargeInactiveSharePointSites = vm.SharePointSites
                .Where(s => s.IsInactiveForPeriod && s.StorageUsedGb >= 1)
                .OrderByDescending(s => s.StorageUsedBytes)
                .Take(20)
                .ToList();

            vm.UnassignedPolicies = vm.IntunePolicies
                .Where(p => p.IsAssigned == false)
                .OrderByDescending(p => p.LastModifiedDateTime ?? p.CreatedDateTime ?? DateTimeOffset.MinValue)
                .Take(30)
                .ToList();

            vm.StaleOrNonCompliantDevices = vm.ManagedDevices
                .Where(d => d.IsStale || d.IsNonCompliant)
                .OrderByDescending(d => d.IsNonCompliant)
                .ThenBy(d => d.LastSyncDateTime ?? DateTimeOffset.MinValue)
                .Take(30)
                .ToList();

            BuildOpportunities(vm);
        }

        private static void BuildJoinableUserInsights(M365GovernanceVm vm, DateTime threshold)
        {
            var usersByUpn = vm.Users
                .Where(u => !string.IsNullOrWhiteSpace(u.UserPrincipalName))
                .GroupBy(u => NormalizeKey(u.UserPrincipalName))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var reportUpns = vm.ExchangeActivity.Select(a => a.UserPrincipalName)
                .Concat(vm.TeamsActivity.Select(a => a.UserPrincipalName))
                .Concat(vm.OneDriveAccounts.Select(a => a.OwnerPrincipalName))
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(NormalizeKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var matches = reportUpns.Count(usersByUpn.ContainsKey);
            vm.CanJoinReportsToUsers = reportUpns.Count > 0 && matches >= Math.Min(10, Math.Max(1, reportUpns.Count / 5));
            vm.UsageIdentityNote = vm.CanJoinReportsToUsers
                ? "Los reportes de uso se pudieron cruzar con usuarios del directorio."
                : "Microsoft 365 puede estar ocultando nombres de usuarios/sitios en reportes. Se muestran métricas agregadas y fuentes disponibles.";

            if (!vm.CanJoinReportsToUsers)
            {
                return;
            }

            var activeCoreUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in vm.ExchangeActivity.Where(a => a.LastActivityDate.HasValue && a.LastActivityDate.Value.Date >= threshold && a.TotalActivityCount > 0))
            {
                activeCoreUsers.Add(NormalizeKey(item.UserPrincipalName));
            }

            foreach (var item in vm.TeamsActivity.Where(a => a.LastActivityDate.HasValue && a.LastActivityDate.Value.Date >= threshold && a.TotalActivityCount > 0))
            {
                activeCoreUsers.Add(NormalizeKey(item.UserPrincipalName));
            }

            foreach (var item in vm.OneDriveAccounts.Where(a => !a.IsInactiveForPeriod && a.ActiveFileCount > 0))
            {
                activeCoreUsers.Add(NormalizeKey(item.OwnerPrincipalName));
            }

            vm.UsersWithoutCoreActivity = vm.Users
                .Where(u => u.AccountEnabled && u.AssignedLicenseCount > 0)
                .Where(u => !activeCoreUsers.Contains(NormalizeKey(u.UserPrincipalName)))
                .OrderByDescending(u => u.AssignedLicenseCount)
                .ThenBy(u => u.DisplayName)
                .Take(40)
                .Select(u => new M365UserOptimizationCandidate
                {
                    DisplayName = u.DisplayName,
                    UserPrincipalName = u.UserPrincipalName,
                    LicenseCount = u.AssignedLicenseCount,
                    Reason = $"Sin actividad visible en Exchange, Teams u OneDrive dentro de {vm.PeriodDays} días."
                })
                .ToList();
        }

        private static void BuildOpportunities(M365GovernanceVm vm)
        {
            if (vm.AvailableLicenses > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Licenciamiento",
                    Title = "Licencias disponibles sin asignar",
                    Count = vm.AvailableLicenses,
                    Severity = vm.AvailableLicenses >= 10 ? "Media" : "Baja",
                    Detail = $"Hay {vm.AvailableLicenses} unidad(es) disponibles entre las suscripciones activas.",
                    RecommendedAction = "Revisar si esas licencias deben asignarse, reservarse o retirarse del contrato."
                });
            }

            if (vm.UsersWithoutCoreActivity.Count > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Adopción",
                    Title = "Usuarios licenciados sin actividad principal",
                    Count = vm.UsersWithoutCoreActivity.Count,
                    Severity = "Alta",
                    Detail = $"Se detectaron {vm.UsersWithoutCoreActivity.Count} usuario(s) con licencia y sin actividad visible en servicios principales.",
                    RecommendedAction = "Validar si son cuentas reales, cuentas de servicio o usuarios que requieren adopción/capacitación."
                });
            }

            foreach (var workload in vm.Workloads.Where(w => w.TotalItems > 0 && w.ActivityPercent < 50))
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = workload.Name,
                    Title = $"Baja adopción de {workload.Name}",
                    Count = workload.InactiveItems,
                    Severity = workload.ActivityPercent < 25 ? "Alta" : "Media",
                    Detail = $"Solo {workload.ActivityPercent:0.#}% de los registros del reporte aparecen activos en el período.",
                    RecommendedAction = "Revisar población licenciada, comunicación interna y necesidades de capacitación por servicio."
                });
            }

            if (vm.LargeInactiveOneDriveAccounts.Count > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "OneDrive",
                    Title = "OneDrive con almacenamiento sin actividad",
                    Count = vm.LargeInactiveOneDriveAccounts.Count,
                    Severity = "Media",
                    Detail = "Hay cuentas con más de 1 GB usado y sin actividad reciente.",
                    RecommendedAction = "Revisar dueños, retención y posible archivado de datos no operativos."
                });
            }

            if (vm.LargeInactiveSharePointSites.Count > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "SharePoint",
                    Title = "Sitios con almacenamiento sin actividad",
                    Count = vm.LargeInactiveSharePointSites.Count,
                    Severity = "Media",
                    Detail = "Hay sitios con más de 1 GB usado y sin actividad reciente.",
                    RecommendedAction = "Validar propietarios, vigencia del sitio y estrategia de archivo/gobierno."
                });
            }

            if (vm.UnassignedPolicies.Count > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Intune",
                    Title = "Políticas creadas sin asignación",
                    Count = vm.UnassignedPolicies.Count,
                    Severity = "Media",
                    Detail = "Existen políticas de Intune o catálogo de configuración sin asignación visible.",
                    RecommendedAction = "Revisar si son borradores, políticas obsoletas o configuraciones pendientes de despliegue."
                });
            }

            if (vm.NonCompliantDevices > 0 || vm.StaleDevices > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Dispositivos",
                    Title = "Dispositivos no conformes o sin sincronización reciente",
                    Count = vm.StaleOrNonCompliantDevices.Count,
                    Severity = vm.NonCompliantDevices > 0 ? "Alta" : "Media",
                    Detail = $"{vm.NonCompliantDevices} no conforme(s) y {vm.StaleDevices} sin sincronización reciente.",
                    RecommendedAction = "Depurar inventario, revisar compliance y contactar usuarios de dispositivos críticos."
                });
            }

            if (vm.ActiveServiceIssues > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Salud Microsoft 365",
                    Title = "Servicios Microsoft con incidencia",
                    Count = vm.ActiveServiceIssues,
                    Severity = "Media",
                    Detail = "Microsoft reporta servicios con estado distinto a operativo.",
                    RecommendedAction = "Comunicar impacto, validar servicios afectados y hacer seguimiento al incidente."
                });
            }

            if (vm.MajorUpcomingChanges > 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Cambios Microsoft",
                    Title = "Cambios próximos que requieren revisión",
                    Count = vm.MajorUpcomingChanges,
                    Severity = "Media",
                    Detail = "Hay mensajes del Message Center marcados como cambio mayor o con fecha de acción.",
                    RecommendedAction = "Asignar responsable y registrar el impacto esperado para usuarios finales."
                });
            }

            if (vm.Purview.LabelsAvailable && vm.Purview.SensitivityLabels == 0)
            {
                vm.Opportunities.Add(new M365OptimizationOpportunity
                {
                    Area = "Purview",
                    Title = "Sin etiquetas de sensibilidad detectadas",
                    Count = 0,
                    Severity = "Media",
                    Detail = "El tenant no muestra etiquetas de sensibilidad en la fuente consultada.",
                    RecommendedAction = "Definir clasificación básica de información si el cliente maneja datos sensibles."
                });
            }
        }

        private async Task<JsonDocument?> TryGetJsonAsync(
            HttpClient client,
            string url,
            M365GovernanceVm vm,
            string source,
            CancellationToken cancellationToken,
            bool registerOnFailure = true)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (registerOnFailure)
                    {
                        RegisterDataSource(vm, source, false, 0, BuildGraphWarning(response.StatusCode, raw));
                    }

                    return null;
                }

                return JsonDocument.Parse(raw);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible consultar la fuente {Source}.", source);
                if (registerOnFailure)
                {
                    RegisterDataSource(vm, source, false, 0, ex.Message);
                }

                return null;
            }
        }

        private async Task<List<Dictionary<string, string>>> FetchCsvReportAsync(
            HttpClient client,
            string url,
            M365GovernanceVm vm,
            string source,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    RegisterDataSource(vm, source, false, 0, BuildGraphWarning(response.StatusCode, raw));
                    return new List<Dictionary<string, string>>();
                }

                return ParseCsv(raw);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible consultar el reporte CSV {Source}.", source);
                RegisterDataSource(vm, source, false, 0, ex.Message);
                return new List<Dictionary<string, string>>();
            }
        }

        private static List<Dictionary<string, string>> ParseCsv(string raw)
        {
            var rows = new List<Dictionary<string, string>>();
            if (string.IsNullOrWhiteSpace(raw)) return rows;

            raw = raw.TrimStart('\uFEFF');
            using var reader = new StringReader(raw);
            var headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine)) return rows;

            var headers = ParseCsvLine(headerLine).Select(h => h.Trim()).ToList();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = ParseCsvLine(line);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                {
                    row[headers[i]] = i < values.Count ? values[i] : "";
                }

                rows.Add(row);
            }

            return rows;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    values.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            values.Add(sb.ToString());
            return values;
        }

        private static void RegisterDataSource(M365GovernanceVm vm, string name, bool isAvailable, int count, string? message)
        {
            if (vm.DataSources.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.IsAvailable == isAvailable))
            {
                return;
            }

            vm.DataSources.Add(new SecurityDataSourceStatus
            {
                Name = name,
                IsAvailable = isAvailable,
                Count = count,
                Message = message ?? (count > 0 ? "Datos actualizados" : "Sin datos para el período")
            });
        }

        private static bool SourceFailed(M365GovernanceVm vm, string name)
        {
            return vm.DataSources.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && !s.IsAvailable);
        }

        private static string BuildGraphWarning(HttpStatusCode statusCode, string raw)
        {
            var detail = TryExtractGraphErrorMessage(raw);
            var baseMessage = statusCode switch
            {
                HttpStatusCode.Unauthorized => "Microsoft Graph devolvió 401. Valida credenciales o token.",
                HttpStatusCode.Forbidden => "Microsoft Graph devolvió 403. Falta consentimiento o permiso para esta fuente.",
                HttpStatusCode.TooManyRequests => "Microsoft Graph limitó temporalmente la consulta. Intenta actualizar en unos minutos.",
                _ => $"Microsoft Graph devolvió {(int)statusCode}."
            };

            return string.IsNullOrWhiteSpace(detail) ? baseMessage : $"{baseMessage} {detail}";
        }

        private static string? TryExtractGraphErrorMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String)
                {
                    var text = message.GetString();
                    return string.IsNullOrWhiteSpace(text)
                        ? null
                        : text.Length > 180 ? text[..180] + "..." : text;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string NormalizePeriod(string period)
        {
            return period?.Trim().ToUpperInvariant() switch
            {
                "D7" => "D7",
                "D30" => "D30",
                "D180" => "D180",
                _ => "D90"
            };
        }

        private static string NormalizeKey(string? value) => (value ?? "").Trim().ToLowerInvariant();

        private static string GetString(Dictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value.Trim() : "";
        }

        private static int GetInt(Dictionary<string, string> row, string key)
        {
            var raw = GetString(row, key);
            return int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static long GetLong(Dictionary<string, string> row, string key)
        {
            var raw = GetString(row, key);
            return long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static bool GetBool(Dictionary<string, string> row, string key)
        {
            var raw = GetString(row, key);
            return bool.TryParse(raw, out var value) && value;
        }

        private static DateTime? GetDate(Dictionary<string, string> row, string key)
        {
            var raw = GetString(row, key);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
                ? value.Date
                : null;
        }
    }

    internal static class M365JsonExtensions
    {
        public static string GetStringOrDefault(this JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                return "";
            }

            return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
        }

        public static int GetIntOrDefault(this JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var value)
                ? value
                : 0;
        }

        public static bool GetBoolOrDefault(this JsonElement element, string name, bool defaultValue = false)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                return defaultValue;
            }

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }

        public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(property.GetString(), out var value))
            {
                return value;
            }

            return null;
        }

        public static string GetArrayAsString(this JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            return string.Join(", ", property.EnumerateArray()
                .Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : i.ToString())
                .Where(i => !string.IsNullOrWhiteSpace(i)));
        }
    }
}
