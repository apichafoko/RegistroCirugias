using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Models;
using RegistroCx.Models.ReportModels;
using RegistroCx.Services.Repositories;

namespace RegistroCx.Services.Reports;

public class ReportDataService
{
    private readonly IAppointmentRepository _appointmentRepo;
    private readonly IUserProfileRepository _userRepo;

    public ReportDataService(IAppointmentRepository appointmentRepo, IUserProfileRepository userRepo)
    {
        _appointmentRepo = appointmentRepo;
        _userRepo = userRepo;
    }

    public async Task<ReportData> GenerateWeeklyReportDataAsync(long chatId, CancellationToken ct = default)
    {
        // Obtener el GoogleEmail del usuario para consultas de equipo
        var userProfile = await _userRepo.GetOrCreateAsync(chatId, ct);
        var googleEmail = userProfile.GoogleEmail;
        
        if (string.IsNullOrWhiteSpace(googleEmail))
        {
            throw new InvalidOperationException("User must have a Google email configured to generate reports");
        }

        var period = ReportPeriod.CreateWeekly();
        var appointments = await _appointmentRepo.GetAppointmentsForWeekAsync(googleEmail, period.StartDate, ct);
        
        return await ProcessAppointmentsIntoReportData(appointments, period, chatId);
    }

    public async Task<ReportData> GenerateMonthlyReportDataAsync(long chatId, int month, int year, CancellationToken ct = default)
    {
        // Obtener el GoogleEmail del usuario para consultas de equipo
        var userProfile = await _userRepo.GetOrCreateAsync(chatId, ct);
        var googleEmail = userProfile.GoogleEmail;
        
        if (string.IsNullOrWhiteSpace(googleEmail))
        {
            throw new InvalidOperationException("User must have a Google email configured to generate reports");
        }

        var period = ReportPeriod.CreateMonthly(month, year);
        var appointments = await _appointmentRepo.GetAppointmentsForMonthAsync(googleEmail, month, year, ct);
        
        return await ProcessAppointmentsIntoReportData(appointments, period, chatId);
    }

    public async Task<ReportData> GenerateAnnualReportDataAsync(long chatId, int year, CancellationToken ct = default)
    {
        // Obtener el GoogleEmail del usuario para consultas de equipo
        var userProfile = await _userRepo.GetOrCreateAsync(chatId, ct);
        var googleEmail = userProfile.GoogleEmail;
        
        if (string.IsNullOrWhiteSpace(googleEmail))
        {
            throw new InvalidOperationException("User must have a Google email configured to generate reports");
        }

        var period = ReportPeriod.CreateAnnual(year);
        var appointments = await _appointmentRepo.GetAppointmentsForYearAsync(googleEmail, year, ct);
        
        return await ProcessAppointmentsIntoReportData(appointments, period, chatId);
    }

    private async Task<ReportData> ProcessAppointmentsIntoReportData(List<Appointment> appointments, ReportPeriod period, long chatId)
    {
        var reportData = new ReportData
        {
            Period = period,
            ChatId = chatId,
            Appointments = appointments,
            TotalSurgeries = appointments.Count
        };

        // Estadísticas básicas
        CalculateBasicStatistics(reportData, appointments);
        
        // Estadísticas de cirujanos
        await CalculateSurgeonStatisticsAsync(reportData, appointments);
        
        // Estadísticas de centros médicos  
        CalculateCenterStatistics(reportData, appointments);
        
        // Colaboraciones
        CalculateCollaborationStatistics(reportData, appointments);
        
        // Timeline/tendencias
        CalculateTimelineStatistics(reportData, appointments);

        return reportData;
    }

    private void CalculateBasicStatistics(ReportData reportData, List<Appointment> appointments)
    {
        // Cirugías por tipo
        reportData.SurgeriesByType = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Cirugia))
            .GroupBy(a => a.Cirugia!.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

        // Cirugías por centro
        reportData.SurgeriesByCenter = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Lugar))
            .GroupBy(a => a.Lugar!)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

        // Cirugías por anestesiólogo
        reportData.SurgeriesByAnesthesiologist = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Anestesiologo))
            .GroupBy(a => a.Anestesiologo!)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

        // Cirugías por día de la semana
        reportData.SurgeriesByDay = appointments
            .Where(a => a.FechaHora.HasValue)
            .GroupBy(a => a.FechaHora!.Value.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

        // Cirugías por hora del día
        reportData.SurgeriesByHour = appointments
            .Where(a => a.FechaHora.HasValue)
            .GroupBy(a => a.FechaHora!.Value.Hour)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));
    }

    private async Task CalculateSurgeonStatisticsAsync(ReportData reportData, List<Appointment> appointments)
    {
        await Task.CompletedTask;
        var surgeonGroups = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Cirujano))
            .GroupBy(a => a.Cirujano!);

        reportData.SurgeonStats = new List<SurgeonStatistics>();
        reportData.SurgeonSpecializations = new Dictionary<string, List<string>>();

        foreach (var surgeonGroup in surgeonGroups)
        {
            var surgeonName = surgeonGroup.Key;
            var surgeonAppointments = surgeonGroup.ToList();

            var stats = new SurgeonStatistics
            {
                SurgeonName = surgeonName,
                TotalSurgeries = surgeonAppointments.Sum(a => a.Cantidad ?? 1)
            };

            // Cirugías por tipo para este cirujano
            stats.SurgeriesByType = surgeonAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Cirugia))
                .GroupBy(a => a.Cirugia!.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

            // Centros donde opera
            stats.PreferredCenters = surgeonAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Lugar))
                .Select(a => a.Lugar!)
                .ToList();

            // Anestesiólogos con los que colabora
            stats.CollaboratingAnesthesiologists = surgeonAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Anestesiologo))
                .Select(a => a.Anestesiologo!)
                .ToList();

            // Horarios preferidos
            stats.PreferredTimes = surgeonAppointments
                .Where(a => a.FechaHora.HasValue)
                .GroupBy(a => a.FechaHora!.Value.TimeOfDay)
                .ToDictionary(g => g.Key, g => g.Count());

            // Días preferidos
            stats.PreferredDays = surgeonAppointments
                .Where(a => a.FechaHora.HasValue)
                .GroupBy(a => a.FechaHora!.Value.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Count());

            reportData.SurgeonStats.Add(stats);

            // Especializaciones (tipos de cirugía más frecuentes por cirujano)
            var topSurgeries = stats.SurgeriesByType
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .Select(kvp => kvp.Key)
                .ToList();
            
            reportData.SurgeonSpecializations[surgeonName] = topSurgeries;
        }

        // Ordenar cirujanos por volumen total
        reportData.SurgeonStats = reportData.SurgeonStats
            .OrderByDescending(s => s.TotalSurgeries)
            .ToList();
    }

    private void CalculateCenterStatistics(ReportData reportData, List<Appointment> appointments)
    {
        var centerGroups = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Lugar))
            .GroupBy(a => a.Lugar!);

        reportData.CenterStats = new List<CenterStatistics>();

        foreach (var centerGroup in centerGroups)
        {
            var centerName = centerGroup.Key;
            var centerAppointments = centerGroup.ToList();

            var stats = new CenterStatistics
            {
                CenterName = centerName,
                TotalSurgeries = centerAppointments.Sum(a => a.Cantidad ?? 1)
            };

            // Cirugías por tipo en este centro
            stats.SurgeriesByType = centerAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Cirugia))
                .GroupBy(a => a.Cirugia!.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

            // Cirujanos activos en este centro
            stats.ActiveSurgeons = centerAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Cirujano))
                .Select(a => a.Cirujano!)
                .ToList();

            // Anestesiólogos activos en este centro
            stats.ActiveAnesthesiologists = centerAppointments
                .Where(a => !string.IsNullOrWhiteSpace(a.Anestesiologo))
                .Select(a => a.Anestesiologo!)
                .ToList();

            // Horarios preferidos en este centro
            stats.PreferredTimes = centerAppointments
                .Where(a => a.FechaHora.HasValue)
                .GroupBy(a => a.FechaHora!.Value.TimeOfDay)
                .ToDictionary(g => g.Key, g => g.Count());

            reportData.CenterStats.Add(stats);
        }

        // Ordenar centros por volumen total
        reportData.CenterStats = reportData.CenterStats
            .OrderByDescending(c => c.TotalSurgeries)
            .ToList();
    }

    private void CalculateCollaborationStatistics(ReportData reportData, List<Appointment> appointments)
    {
        var collaborations = appointments
            .Where(a => !string.IsNullOrWhiteSpace(a.Cirujano) && !string.IsNullOrWhiteSpace(a.Anestesiologo))
            .GroupBy(a => new { Surgeon = a.Cirujano!, Anesthesiologist = a.Anestesiologo! })
            .Select(g => new CollaborationPair
            {
                SurgeonName = g.Key.Surgeon,
                AnesthesiologistName = g.Key.Anesthesiologist,
                CollaborationCount = g.Sum(a => a.Cantidad ?? 1),
                SurgeryTypes = g.Where(a => !string.IsNullOrWhiteSpace(a.Cirugia))
                               .Select(a => a.Cirugia!.ToUpperInvariant())
                               .ToList(),
                Centers = g.Where(a => !string.IsNullOrWhiteSpace(a.Lugar))
                          .Select(a => a.Lugar!)
                          .ToList()
            })
            .OrderByDescending(c => c.CollaborationCount)
            .ToList();

        reportData.TopCollaborations = collaborations;
    }

    private void CalculateTimelineStatistics(ReportData reportData, List<Appointment> appointments)
    {
        // Timeline diario (para reportes semanales y mensuales)
        reportData.DailySurgeriesTimeline = appointments
            .Where(a => a.FechaHora.HasValue)
            .GroupBy(a => a.FechaHora!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));

        // Timeline mensual (para reportes anuales)
        if (reportData.Period.Type == ReportType.Annual)
        {
            reportData.MonthlySurgeriesTimeline = appointments
                .Where(a => a.FechaHora.HasValue)
                .GroupBy(a => a.FechaHora!.Value.Month)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Cantidad ?? 1));
        }
    }
}