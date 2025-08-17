using System;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Models;
using RegistroCx.models;

namespace RegistroCx.Services.Repositories;

public interface IAppointmentRepository
{
    // Métodos principales con soporte para equipos
    Task<long> SaveAsync(Appointment appointment, int equipoId, CancellationToken ct);
    Task<long> SaveAsync(Appointment appointment, long chatId, CancellationToken ct); // Para compatibilidad temporal
    Task<Appointment?> GetByIdAsync(long id, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task UpdateCalendarEventAsync(long appointmentId, string eventId, CancellationToken ct);
    Task UpdateAsync(long appointmentId, ModificationRequest changes, CancellationToken ct = default);
    
    // Métodos con equipo_id
    Task<List<Appointment>> GetPendingCalendarSyncAsync(int equipoId, CancellationToken ct);
    Task<List<Appointment>> GetByEquipoAndDateRangeAsync(int equipoId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    Task<List<Appointment>> GetAppointmentsByDateRangeAsync(int equipoId, DateTime startDate, DateTime endDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForWeekAsync(int equipoId, DateTime weekStartDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForMonthAsync(int equipoId, int month, int year, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForYearAsync(int equipoId, int year, CancellationToken ct);
    
    // Métodos para recordatorios
    Task<List<Appointment>> GetAppointmentsNeedingRemindersAsync(CancellationToken ct);
    Task MarkReminderSentAsync(long appointmentId, CancellationToken ct);
    
    // Métodos de compatibilidad temporal (mantener durante migración)
    Task<List<Appointment>> GetPendingCalendarSyncAsync(long chatId, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsByDateRangeAsync(string googleEmail, DateTime startDate, DateTime endDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForWeekAsync(string googleEmail, DateTime weekStartDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForMonthAsync(string googleEmail, int month, int year, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForYearAsync(string googleEmail, int year, CancellationToken ct);
    Task<List<Appointment>> GetByUserAndDateRangeAsync(long chatId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    
    // Métodos de migración
    Task MigrateAppointmentsToEquipoAsync(long chatId, int equipoId, CancellationToken ct = default);
    Task<List<Appointment>> GetAppointmentsWithoutEquipoAsync(CancellationToken ct = default);
}