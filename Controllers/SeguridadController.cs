using Microsoft.AspNetCore.Mvc;
using DigitalTechClientPortal.Models;
using System;
using System.Collections.Generic;

namespace DigitalTechClientPortal.Controllers
{
    public class SeguridadController : Controller
    {
        public IActionResult Index()
        {
            var vm = new SeguridadVm
            {
                SecureScoreCurrent = 78,
                AdoptionScoreCurrent = 64,
                ThreatsLast30Days = 12,
                SecureScoreDelta = 3,
                AdoptionScoreDelta = 2,
                TimelineLabels = new() { "Mar", "Abr", "May", "Jun", "Jul", "Ago" },
                SecureScoreSeries = new() { 65, 68, 70, 72, 75, 78 },
                AdoptionScoreSeries = new() { 50, 55, 58, 60, 62, 64 },
                Recommendations = new()
                {
                    ("Habilitar MFA para administradores restantes", 6),
                    ("Bloquear protocolos heredados (IMAP/POP)", 4),
                    ("Etiquetado automático de datos sensibles", 3)
                },
                ThreatsTable = new()
                {
                    new SeguridadVm.ThreatItem { Fecha = DateTime.Today.AddDays(-2), Tipo = "Phishing", Severidad = "Alta", Estado = "Mitigada" },
                    new SeguridadVm.ThreatItem { Fecha = DateTime.Today.AddDays(-5), Tipo = "Malware", Severidad = "Media", Estado = "En investigación" },
                    new SeguridadVm.ThreatItem { Fecha = DateTime.Today.AddDays(-10), Tipo = "Login sospechoso", Severidad = "Alta", Estado = "Mitigada" },
                    new SeguridadVm.ThreatItem { Fecha = DateTime.Today.AddDays(-15), Tipo = "Ataque fuerza bruta", Severidad = "Baja", Estado = "Bloqueada" }
                }
            };

            return View(vm);
        }
    }
}