using System;

namespace RegistroCx.Models.ReportModels;

public enum ReportType
{
    Weekly,
    Monthly, 
    Annual
}

public class ReportPeriod
{
    public ReportType Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    
    // Factory methods para crear períodos estándar
    public static ReportPeriod CreateWeekly()
    {
        var endDate = DateTime.Today;
        var startDate = endDate.AddDays(-6); // Últimos 7 días (incluye hoy)
        
        return new ReportPeriod
        {
            Type = ReportType.Weekly,
            StartDate = startDate,
            EndDate = endDate,
            DisplayName = $"Semana del {startDate:dd/MM} al {endDate:dd/MM/yyyy}"
        };
    }
    
    public static ReportPeriod CreateMonthly(int month, int year)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);
        
        return new ReportPeriod
        {
            Type = ReportType.Monthly,
            StartDate = startDate,
            EndDate = endDate,
            DisplayName = $"{GetMonthName(month)} {year}"
        };
    }
    
    public static ReportPeriod CreateAnnual(int year)
    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = new DateTime(year, 12, 31);
        
        return new ReportPeriod
        {
            Type = ReportType.Annual,
            StartDate = startDate,
            EndDate = endDate,
            DisplayName = $"Año {year}"
        };
    }
    
    private static string GetMonthName(int month)
    {
        return month switch
        {
            1 => "Enero",
            2 => "Febrero", 
            3 => "Marzo",
            4 => "Abril",
            5 => "Mayo",
            6 => "Junio",
            7 => "Julio",
            8 => "Agosto",
            9 => "Septiembre",
            10 => "Octubre",
            11 => "Noviembre",
            12 => "Diciembre",
            _ => "Mes desconocido"
        };
    }
}