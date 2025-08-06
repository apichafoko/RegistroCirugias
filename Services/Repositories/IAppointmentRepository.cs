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
}