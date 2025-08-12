using System;
using System.Collections.Generic;
using System.Linq;

namespace RegistroCx.Models.ReportModels;

public class SurgeonStatistics
{
    public string SurgeonName { get; set; } = string.Empty;
    public int TotalSurgeries { get; set; }
    public Dictionary<string, int> SurgeriesByType { get; set; } = new();
    public List<string> PreferredCenters { get; set; } = new();
    public List<string> CollaboratingAnesthesiologists { get; set; } = new();
    public Dictionary<TimeSpan, int> PreferredTimes { get; set; } = new();
    public Dictionary<DayOfWeek, int> PreferredDays { get; set; } = new();
    
    // Propiedades calculadas
    public string TopSurgeryType => SurgeriesByType.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "N/A";
    public int TopSurgeryTypeCount => SurgeriesByType.OrderByDescending(x => x.Value).FirstOrDefault().Value;
    public string PreferredCenter => PreferredCenters.GroupBy(c => c).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "N/A";
    public string TopAnesthesiologist => CollaboratingAnesthesiologists.GroupBy(a => a).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "N/A";
    public DayOfWeek PreferredDay => PreferredDays.OrderByDescending(x => x.Value).FirstOrDefault().Key;
    public TimeSpan PreferredTime => PreferredTimes.OrderByDescending(x => x.Value).FirstOrDefault().Key;
    
    // Métricas adicionales
    public double AverageSurgeriesPerDay => TotalSurgeries > 0 && PreferredDays.Count > 0 
        ? Math.Round((double)TotalSurgeries / PreferredDays.Sum(x => x.Value), 2) 
        : 0;
    
    public int UniqueAnesthesiologists => CollaboratingAnesthesiologists.Distinct().Count();
    public int UniqueCenters => PreferredCenters.Distinct().Count();
    public int UniqueSurgeryTypes => SurgeriesByType.Count;
}

public class CollaborationPair
{
    public string SurgeonName { get; set; } = string.Empty;
    public string AnesthesiologistName { get; set; } = string.Empty;
    public int CollaborationCount { get; set; }
    public List<string> SurgeryTypes { get; set; } = new();
    public List<string> Centers { get; set; } = new();
    
    public string DisplayName => $"{SurgeonName} + {AnesthesiologistName}";
    public string TopSurgeryType => SurgeryTypes.GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "N/A";
}

public class CenterStatistics
{
    public string CenterName { get; set; } = string.Empty;
    public int TotalSurgeries { get; set; }
    public Dictionary<string, int> SurgeriesByType { get; set; } = new();
    public List<string> ActiveSurgeons { get; set; } = new();
    public List<string> ActiveAnesthesiologists { get; set; } = new();
    public Dictionary<TimeSpan, int> PreferredTimes { get; set; } = new();
    
    public string TopSurgeryType => SurgeriesByType.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "N/A";
    public string TopSurgeon => ActiveSurgeons.GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "N/A";
    public int UniqueSurgeons => ActiveSurgeons.Distinct().Count();
    public int UniqueAnesthesiologists => ActiveAnesthesiologists.Distinct().Count();
}