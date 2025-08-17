using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.models;
using RegistroCx.Models;
using RegistroCx.Services.Repositories;

namespace RegistroCx.Services
{
    public class AppointmentSearchService
    {
        private readonly IAppointmentRepository _appointmentRepo;

        public AppointmentSearchService(IAppointmentRepository appointmentRepo)
        {
            _appointmentRepo = appointmentRepo;
        }

        public async Task<AppointmentSearchResult> FindCandidatesAsync(
            long chatId, 
            string searchText, 
            DateTime? contextDate = null)
        {
            var result = new AppointmentSearchResult();
            
            Console.WriteLine($"[SEARCH] Searching for: '{searchText}' for user {chatId}");
            
            // Obtener appointments del usuario (últimos 30 días atrás y próximos 365 días adelante)
            // Esto permite buscar cirugías futuras programadas para el próximo año
            var startDate = DateTime.Today.AddDays(-30);
            var endDate = DateTime.Today.AddDays(365);
            var userAppointments = await _appointmentRepo.GetByUserAndDateRangeAsync(chatId, startDate, endDate);

            Console.WriteLine($"[SEARCH] Found {userAppointments.Count()} appointments in date range {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}");
            
            if (!userAppointments.Any())
            {
                Console.WriteLine("[SEARCH] No appointments found in date range");
                return result; // NotFound = true
            }

            var candidates = new List<(Appointment appointment, int score)>();

            foreach (var appointment in userAppointments)
            {
                var score = CalculateMatchScore(appointment, searchText, contextDate);
                Console.WriteLine($"[SEARCH] Appointment: {appointment.Cirujano} on {appointment.FechaHora:dd/MM/yyyy HH:mm} - Score: {score}");
                
                if (score > 0)
                {
                    candidates.Add((appointment, score));
                }
            }

            // Ordenar por score descendente y tomar los mejores matches
            result.Candidates = candidates
                .OrderByDescending(c => c.score)
                .ThenBy(c => c.appointment.FechaHora) // Ordenar por fecha como criterio secundario
                .Take(3) // Máximo 3 candidatos para evitar confusión
                .Select(c => c.appointment)
                .ToList();

            return result;
        }

        private int CalculateMatchScore(Appointment appointment, string searchText, DateTime? contextDate)
        {
            var text = searchText.ToLowerInvariant();
            var score = 0;

            // Puntuación por fecha
            score += CalculateDateScore(appointment, text, contextDate);
            
            // Puntuación por cirujano
            score += CalculateSurgeonScore(appointment, text);
            
            // Puntuación por hora
            score += CalculateTimeScore(appointment, text);
            
            // Puntuación por tipo de cirugía
            score += CalculateSurgeryTypeScore(appointment, text);
            
            // Puntuación por lugar
            score += CalculateLocationScore(appointment, text);

            return score;
        }

        private int CalculateDateScore(Appointment appointment, string text, DateTime? contextDate)
        {
            if (!appointment.FechaHora.HasValue) return 0;

            var appointmentDate = appointment.FechaHora.Value.Date;
            var score = 0;

            // Días de la semana
            var dayOfWeek = appointmentDate.ToString("dddd", new CultureInfo("es-ES")).ToLower();
            if (text.Contains(dayOfWeek))
                score += 50;

            // Nombres de días abreviados
            var dayMappings = new Dictionary<string, DayOfWeek>
            {
                { "lunes", DayOfWeek.Monday },
                { "martes", DayOfWeek.Tuesday },
                { "miércoles", DayOfWeek.Wednesday },
                { "miercoles", DayOfWeek.Wednesday },
                { "jueves", DayOfWeek.Thursday },
                { "viernes", DayOfWeek.Friday },
                { "sábado", DayOfWeek.Saturday },
                { "sabado", DayOfWeek.Saturday },
                { "domingo", DayOfWeek.Sunday }
            };

            foreach (var mapping in dayMappings)
            {
                if (text.Contains(mapping.Key) && appointmentDate.DayOfWeek == mapping.Value)
                {
                    score += 50;
                    break;
                }
            }

            // Referencias temporales relativas
            if (contextDate.HasValue)
            {
                var daysDiff = (appointmentDate - contextDate.Value.Date).Days;
                
                if (text.Contains("hoy") && daysDiff == 0)
                    score += 60;
                else if (text.Contains("mañana") && daysDiff == 1)
                    score += 60;
                else if (text.Contains("pasado mañana") && daysDiff == 2)
                    score += 60;
            }

            // Fechas específicas (dd/mm, dd-mm, etc.)
            var datePatterns = new[]
            {
                @"\b(\d{1,2})[\/\-](\d{1,2})\b",  // 23/09, 23-09
                @"\b(\d{1,2})\s+de\s+\w+\b"      // 23 de septiembre
            };

            foreach (var pattern in datePatterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var day) && match.Groups.Count > 2 && int.TryParse(match.Groups[2].Value, out var month))
                {
                    // Coincidencia exacta de día y mes
                    if (appointmentDate.Day == day && appointmentDate.Month == month)
                    {
                        score += 80; // Alta puntuación para coincidencia exacta de fecha
                        Console.WriteLine($"[SEARCH] Exact date match found: {day}/{month} for appointment on {appointmentDate:dd/MM/yyyy}");
                    }
                    // Solo día coincide
                    else if (appointmentDate.Day == day)
                    {
                        score += 20; // Menor puntuación solo por día
                    }
                }
            }

            return score;
        }

        private int CalculateSurgeonScore(Appointment appointment, string text)
        {
            if (string.IsNullOrEmpty(appointment.Cirujano)) return 0;

            var surgeonName = appointment.Cirujano.ToLowerInvariant();
            var surgeonWords = surgeonName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var score = 0;
            foreach (var word in surgeonWords)
            {
                if (text.Contains(word))
                {
                    score += word.Length >= 3 ? 30 : 15; // Palabras más largas tienen más peso
                }
            }

            return score;
        }

        private int CalculateTimeScore(Appointment appointment, string text)
        {
            if (!appointment.FechaHora.HasValue) return 0;

            var timeStr = appointment.FechaHora.Value.ToString("HH:mm");
            var hour = appointment.FechaHora.Value.Hour;

            // Buscar patrones de hora en el texto
            var timePatterns = new[]
            {
                @"\b(\d{1,2}):(\d{2})\b",
                @"\b(\d{1,2})hs?\b",
                @"\b(\d{1,2})\s*h\b"
            };

            foreach (var pattern in timePatterns)
            {
                var matches = Regex.Matches(text, pattern);
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var textHour))
                    {
                        if (textHour == hour)
                            return 40;
                    }
                }
            }

            return 0;
        }

        private int CalculateSurgeryTypeScore(Appointment appointment, string text)
        {
            if (string.IsNullOrEmpty(appointment.Cirugia)) return 0;

            var surgeryType = appointment.Cirugia.ToLowerInvariant();
            
            // Buscar coincidencias exactas o parciales
            if (text.Contains(surgeryType))
                return 25;

            // Buscar siglas comunes
            var commonAcronyms = new Dictionary<string, string[]>
            {
                { "cers", new[] { "cesárea", "cesarea" } },
                { "mld", new[] { "minilaparotomía", "minilaparotomia" } },
                { "hyst", new[] { "histerectomía", "histerectomia" } }
            };

            foreach (var acronym in commonAcronyms)
            {
                if (text.Contains(acronym.Key))
                {
                    foreach (var fullName in acronym.Value)
                    {
                        if (surgeryType.Contains(fullName))
                            return 20;
                    }
                }
            }

            return 0;
        }

        private int CalculateLocationScore(Appointment appointment, string text)
        {
            if (string.IsNullOrEmpty(appointment.Lugar)) return 0;

            var location = appointment.Lugar.ToLowerInvariant();
            var locationWords = location.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var score = 0;
            foreach (var word in locationWords)
            {
                if (text.Contains(word) && word.Length >= 3)
                {
                    score += 15;
                }
            }

            return score;
        }
    }
}