using System;

namespace DigitalTechClientPortal.Models
{
    public sealed class CreateCalendarEventRequest
    {
        public string Subject { get; set; } = string.Empty;
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
    }
}