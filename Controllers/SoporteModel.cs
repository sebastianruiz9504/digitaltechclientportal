using System.Collections.Generic;

namespace DigitalTechApp.Models
{
    public class SoporteModel
    {
        // Entrada del usuario
        public string? UserMessage { get; set; }

        // Respuesta del agente (solo basada en documentos)
        public string? AgentResponse { get; set; }

        // JSON (o texto) de diagnóstico de búsqueda
        public string? SearchJson { get; set; }

        // Sugerencias (títulos de documentos)
        public List<string> Suggestions { get; set; } = new();

        // Errores de diagnóstico
        public List<string> Errors { get; set; } = new();
    }
}