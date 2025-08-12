using System;
using System.Collections.Generic;
using System.Linq;
using RegistroCx.Models;

namespace RegistroCx.Models.ReportModels;

public class ReportData
{
    public ReportPeriod Period { get; set; } = new();
    public long ChatId { get; set; }
    public List<Appointment> Appointments { get; set; } = new();
    
    // Estadísticas generales
    public int TotalSurgeries { get; set; }
    public Dictionary<string, int> SurgeriesByType { get; set; } = new();
    public Dictionary<string, int> SurgeriesByCenter { get; set; } = new();
    public Dictionary<string, int> SurgeriesByAnesthesiologist { get; set; } = new();
    public Dictionary<DayOfWeek, int> SurgeriesByDay { get; set; } = new();
    public Dictionary<int, int> SurgeriesByHour { get; set; } = new(); // Hora del día
    
    // Estadísticas de cirujanos (NUEVO)
    public List<SurgeonStatistics> SurgeonStats { get; set; } = new();
    public Dictionary<string, List<string>> SurgeonSpecializations { get; set; } = new();
    public List<CollaborationPair> TopCollaborations { get; set; } = new();
    
    // Estadísticas de centros médicos
    public List<CenterStatistics> CenterStats { get; set; } = new();
    
    // Métricas calculadas
    public string MostFrequentSurgeryType => SurgeriesByType.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "N/A";
    public string MostFrequentCenter => SurgeriesByCenter.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "N/A";
    public string MostFrequentAnesthesiologist => SurgeriesByAnesthesiologist.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "N/A";
    public DayOfWeek MostActiveDay => SurgeriesByDay.OrderByDescending(x => x.Value).FirstOrDefault().Key;
    public int MostActiveHour => SurgeriesByHour.OrderByDescending(x => x.Value).FirstOrDefault().Key;
    
    // Métricas de cirujanos
    public string MostActiveSurgeon => SurgeonStats.OrderByDescending(s => s.TotalSurgeries).FirstOrDefault()?.SurgeonName ?? "N/A";
    public CollaborationPair TopCollaboration => TopCollaborations.OrderByDescending(c => c.CollaborationCount).FirstOrDefault() ?? new CollaborationPair();
    
    // Tendencias (para reportes más largos)
    public Dictionary<DateTime, int> DailySurgeriesTimeline { get; set; } = new(); // Para reporte semanal/mensual
    public Dictionary<int, int> MonthlySurgeriesTimeline { get; set; } = new(); // Para reporte anual (mes 1-12)
    
    // Métodos helper para generar métricas
    public double AverageSurgeriesPerDay => Period.Type switch
    {
        ReportType.Weekly => TotalSurgeries / 7.0,
        ReportType.Monthly => TotalSurgeries / (double)(Period.EndDate - Period.StartDate).Days,
        ReportType.Annual => TotalSurgeries / 365.0,
        _ => 0
    };
    
    public string GetPeriodSummary()
    {
        var summary = $"📊 **Resumen {Period.DisplayName}**\n\n";
        summary += $"• **Total cirugías:** {TotalSurgeries}\n";
        summary += $"• **Promedio diario:** {AverageSurgeriesPerDay:F1}\n";
        summary += $"• **Cirugía más frecuente:** {MostFrequentSurgeryType}\n";
        summary += $"• **Centro principal:** {MostFrequentCenter}\n";
        summary += $"• **Cirujano más activo:** {MostActiveSurgeon}\n";
        summary += $"• **Día más activo:** {GetDayName(MostActiveDay)}\n";
        summary += $"• **Horario preferido:** {MostActiveHour:D2}:00\n";
        
        return summary;
    }
    
    private static string GetDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "Lunes",
            DayOfWeek.Tuesday => "Martes", 
            DayOfWeek.Wednesday => "Miércoles",
            DayOfWeek.Thursday => "Jueves",
            DayOfWeek.Friday => "Viernes",
            DayOfWeek.Saturday => "Sábado",
            DayOfWeek.Sunday => "Domingo",
            _ => "Desconocido"
        };
    }
}