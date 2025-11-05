namespace DigitalTechClientPortal.Models
{
    public sealed class FullCalendarEventDto
    {
        public string id { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string start { get; set; } = string.Empty;  // ISO 8601
        public string end   { get; set; } = string.Empty;  // ISO 8601
    }
}