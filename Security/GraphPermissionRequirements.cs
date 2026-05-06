namespace DigitalTechClientPortal.Security
{
    public static class GraphPermissionRequirements
    {
        public static readonly string[] DefaultLoginScopes =
        {
            "openid",
            "profile",
            "email",
            "offline_access",
            "User.Read",
            "User.Read.All",
            "Directory.Read.All",
            "SecurityEvents.Read.All"
        };

        public static readonly string[] LoginScopes =
        {
            "openid",
            "profile",
            "email",
            "offline_access",
            "User.Read",
            "User.Read.All",
            "Directory.Read.All",
            "SecurityEvents.Read.All",
            "SecurityAlert.Read.All",
            "SecurityIncident.Read.All",
            "IdentityRiskyUser.Read.All",
            "AttackSimulation.Read.All",
            "Reports.Read.All",
            "DeviceManagementConfiguration.Read.All",
            "DeviceManagementManagedDevices.Read.All",
            "Application.Read.All",
            "ServiceHealth.Read.All",
            "ServiceMessage.Read.All",
            "eDiscovery.Read.All",
            "InformationProtectionPolicy.Read.All"
        };

        public static readonly string[] SecurityPanelScopes =
        {
            "User.Read.All",
            "Directory.Read.All",
            "SecurityEvents.Read.All",
            "SecurityAlert.Read.All",
            "SecurityIncident.Read.All",
            "IdentityRiskyUser.Read.All",
            "AttackSimulation.Read.All"
        };

        public static readonly string[] GovernanceScopes =
        {
            "User.Read.All",
            "Directory.Read.All",
            "Reports.Read.All",
            "DeviceManagementConfiguration.Read.All",
            "DeviceManagementManagedDevices.Read.All",
            "Application.Read.All",
            "ServiceHealth.Read.All",
            "ServiceMessage.Read.All",
            "eDiscovery.Read.All",
            "InformationProtectionPolicy.Read.All"
        };

        public static readonly string[] TenantReadScopes =
            SecurityPanelScopes
                .Concat(GovernanceScopes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static readonly IReadOnlyDictionary<string, string> SecurityScopeDescriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User.Read.All"] = "Leer usuarios del tenant",
                ["Directory.Read.All"] = "Leer información del directorio",
                ["SecurityEvents.Read.All"] = "Leer alertas legacy, Secure Score y controles",
                ["SecurityAlert.Read.All"] = "Leer alertas de seguridad modernas",
                ["SecurityIncident.Read.All"] = "Leer incidentes de Microsoft 365 Defender",
                ["IdentityRiskyUser.Read.All"] = "Leer usuarios en riesgo de Identity Protection",
                ["AttackSimulation.Read.All"] = "Leer simulaciones de ataque"
            };

        public static readonly IReadOnlyDictionary<string, string> GovernanceScopeDescriptions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User.Read.All"] = "Leer usuarios del tenant",
                ["Directory.Read.All"] = "Leer directorio y licencias",
                ["Reports.Read.All"] = "Leer reportes de uso Microsoft 365",
                ["DeviceManagementConfiguration.Read.All"] = "Leer politicas de Intune",
                ["DeviceManagementManagedDevices.Read.All"] = "Leer dispositivos administrados",
                ["Application.Read.All"] = "Leer aplicaciones empresariales",
                ["ServiceHealth.Read.All"] = "Leer salud de servicios Microsoft 365",
                ["ServiceMessage.Read.All"] = "Leer Message Center",
                ["eDiscovery.Read.All"] = "Leer senales eDiscovery",
                ["InformationProtectionPolicy.Read.All"] = "Leer etiquetas de sensibilidad"
            };

        public static string ToGraphScope(string scope)
        {
            return scope.StartsWith("https://graph.microsoft.com/", StringComparison.OrdinalIgnoreCase)
                ? scope
                : $"https://graph.microsoft.com/{scope}";
        }
    }
}
