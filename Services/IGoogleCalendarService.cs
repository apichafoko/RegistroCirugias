using System;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Models;

namespace RegistroCx.Services;

public interface IGoogleCalendarService
{
    Task<string> CreateAppointmentEventAsync(
        Appointment appointment, 
        long chatId, 
        CancellationToken ct);
        
    Task<bool> SendCalendarInviteAsync(
        string eventId, 
        string recipientEmail, 
        long chatId, 
        CancellationToken ct);
        
    Task DeleteEventAsync(
        string eventId, 
        long chatId, 
        CancellationToken ct);
}