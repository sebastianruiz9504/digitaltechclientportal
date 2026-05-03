namespace DigitalTechClientPortal.Models
{
    // Alertas
    public class SecurityAlert
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Category { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset? CreatedDateTime { get; set; }
        public DateTimeOffset? LastUpdatedDateTime { get; set; }
        public DateTimeOffset? FirstActivityDateTime { get; set; }
        public DateTimeOffset? LastActivityDateTime { get; set; }
        public string Provider { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string ServiceSource { get; set; } = "";
        public string DetectionSource { get; set; } = "";
        public string IncidentId { get; set; } = "";
        public string AlertWebUrl { get; set; } = "";
        public string IncidentWebUrl { get; set; } = "";
        public string RecommendedActions { get; set; } = "";
        public string MitreTechniques { get; set; } = "";
        public string Description { get; set; } = "";
        public List<SecurityAlertEvidence> Evidence { get; set; } = new();
    }

    public class SecurityAlertEvidence
    {
        public string Type { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string ProcessCommandLine { get; set; } = "";
        public string Mailbox { get; set; } = "";
        public string Roles { get; set; } = "";
        public string Verdict { get; set; } = "";
    }

    // Incidentes
    public class SecurityIncident
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset? CreatedDateTime { get; set; }
        public DateTimeOffset? LastUpdatedDateTime { get; set; }
        public int? AlertCount { get; set; }
        public string AssignedTo { get; set; } = "";
        public string Classification { get; set; } = "";
        public string Determination { get; set; } = "";
        public string Description { get; set; } = "";
        public string Summary { get; set; } = "";
        public string IncidentWebUrl { get; set; } = "";
    }

    // Usuarios en riesgo
    public class RiskyUser
    {
        public string UserId { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string RiskLevel { get; set; } = "";
        public string RiskState { get; set; } = "";
        public string RiskDetail { get; set; } = "";
        public DateTimeOffset? LastUpdatedDateTime { get; set; }
    }

    // Dispositivos en riesgo
    public class DeviceSecurityState
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string LoggedOnUsers { get; set; } = "";
        public string RiskScore { get; set; } = "";
        public string MalwareState { get; set; } = "";
        public string OS { get; set; } = "";
        public DateTimeOffset? LastSeenDateTime { get; set; }
    }

    // Secure Score (crudo)
    public class SecureScore
    {
        public string Id { get; set; } = "";
        public double? ActiveUserCount { get; set; }
        public double? CurrentScore { get; set; }
        public double? MaxScore { get; set; }
        public string EnabledServices { get; set; } = "";
        public DateTimeOffset? CreatedDateTime { get; set; }
    }

    // Controles de Secure Score
    public class SecureScoreControl
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string ControlCategory { get; set; } = "";
        public string ActionType { get; set; } = "";
        // Campos removidos de la UI: ImplementationCost, ControlStateUpdates
        public string ImplementationCost { get; set; } = "";
        public string ControlStateUpdates { get; set; } = "";
        public double? MaxScore { get; set; }
    }

    // Simulaciones de ataque
    public class AttackSimulation
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Status { get; set; } = "";
        public string SimulationType { get; set; } = "";
        public DateTimeOffset? LaunchDateTime { get; set; }
        public DateTimeOffset? CompletionDateTime { get; set; }
        public string Payload { get; set; } = "";
    }

    public class SecurityAiPlan
    {
        public string ExecutiveSummary { get; set; } = "";
        public string TenantRiskLevel { get; set; } = "";
        public string TenantRiskRationale { get; set; } = "";
        public string GeneratedAtLocal { get; set; } = "";
        public List<SecurityAiThreat> Threats { get; set; } = new();
        public List<SecurityAiAction> Actions { get; set; } = new();
        public List<string> MissingData { get; set; } = new();
        public List<string> Assumptions { get; set; } = new();
    }

    public class SecurityAiThreat
    {
        public string Title { get; set; } = "";
        public string PlainLanguageSummary { get; set; } = "";
        public string ThreatTranslation { get; set; } = "";
        public string AffectedUser { get; set; } = "";
        public string AffectedDevice { get; set; } = "";
        public string Date { get; set; } = "";
        public string Severity { get; set; } = "";
        public string BusinessImpact { get; set; } = "";
        public string Evidence { get; set; } = "";
        public string ImmediateAction { get; set; } = "";
    }

    public class SecurityAiAction
    {
        public string Priority { get; set; } = "";
        public string Title { get; set; } = "";
        public string WhyItMatters { get; set; } = "";
        public string ExpectedOutcome { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Effort { get; set; } = "";
        public List<SecurityAiStep> Steps { get; set; } = new();
    }

    public class SecurityAiStep
    {
        public int Order { get; set; }
        public string Portal { get; set; } = "";
        public string ClickPath { get; set; } = "";
        public string Instruction { get; set; } = "";
        public string Validation { get; set; } = "";
    }
}
