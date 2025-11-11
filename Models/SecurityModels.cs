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
        public string Provider { get; set; } = "";
        public string Description { get; set; } = "";
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
}