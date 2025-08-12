using System;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Models;

namespace RegistroCx.Services.Repositories;

public interface IAppointmentRepository
{
    Task<long> SaveAsync(Appointment appointment, long chatId, CancellationToken ct);
    Task<Appointment?> GetByIdAsync(long id, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task UpdateCalendarEventAsync(long appointmentId, string eventId, CancellationToken ct);
    Task<List<Appointment>> GetPendingCalendarSyncAsync(long chatId, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsNeedingRemindersAsync(CancellationToken ct);
    Task MarkReminderSentAsync(long appointmentId, CancellationToken ct);
    
    // Métodos para reportes (por email para equipos compartidos)
    Task<List<Appointment>> GetAppointmentsByDateRangeAsync(string googleEmail, DateTime startDate, DateTime endDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForWeekAsync(string googleEmail, DateTime weekStartDate, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForMonthAsync(string googleEmail, int month, int year, CancellationToken ct);
    Task<List<Appointment>> GetAppointmentsForYearAsync(string googleEmail, int year, CancellationToken ct);
}