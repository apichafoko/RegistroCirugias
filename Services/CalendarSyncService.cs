using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Services.Repositories;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services;

public class CalendarSyncService
{
    private readonly IAppointmentRepository _appointmentRepo;
    private readonly IGoogleCalendarService _calendarService;
    private readonly EquipoService _equipoService;

    public CalendarSyncService(
        IAppointmentRepository appointmentRepo, 
        IGoogleCalendarService calendarService,
        EquipoService equipoService)
    {
        _appointmentRepo = appointmentRepo;
        _calendarService = calendarService;
        _equipoService = equipoService;
    }

    /// <summary>
    /// Sincroniza appointments pendientes con Google Calendar después de re-autorización
    /// </summary>
    public async Task<int> SyncPendingAppointmentsAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[CALENDAR-SYNC] Starting sync for chat {chatId}");
            
            // Resolver chatId a equipoId para el nuevo sistema de equipos
            var equipoId = await _equipoService.ObtenerPrimerEquipoIdPorChatIdAsync(chatId, ct);
            
            // Obtener appointments que no tienen calendar_event_id
            var pendingAppointments = await _appointmentRepo.GetPendingCalendarSyncAsync(equipoId, ct);
            
            if (pendingAppointments.Count == 0)
            {
                Console.WriteLine($"[CALENDAR-SYNC] No pending appointments found for team {equipoId}");
                return 0;
            }

            await MessageSender.SendWithRetry(chatId,
                $"🔄 Encontré {pendingAppointments.Count} cirugia(s) que no están en tu calendario. Sincronizando...",
                cancellationToken: ct);

            int syncedCount = 0;
            int failedCount = 0;

            foreach (var appointment in pendingAppointments)
            {
                try
                {
                    Console.WriteLine($"[CALENDAR-SYNC] Syncing appointment: {appointment.Cirugia} on {appointment.FechaHora}");
                    
                    // Crear el evento en Google Calendar
                    var eventId = await _calendarService.CreateAppointmentEventAsync(appointment, chatId, ct);
                    
                    // Actualizar la BD con el calendar_event_id
                    await _appointmentRepo.UpdateCalendarEventAsync(appointment.Id, eventId, ct);
                    
                    syncedCount++;
                    Console.WriteLine($"[CALENDAR-SYNC] ✅ Synced appointment ID {appointment.Id} with calendar event {eventId}");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Console.WriteLine($"[CALENDAR-SYNC] ❌ Failed to sync appointment ID {appointment.Id}: {ex.Message}");
                }
            }

            // Enviar resumen al usuario
            var resultMessage = syncedCount > 0
                ? $"✅ {syncedCount} cirugia(s) sincronizada(s) exitosamente con tu calendario."
                : "⚠️ No se pudieron sincronizar las cirugia(s) con el calendario.";

            if (failedCount > 0)
            {
                resultMessage += $"\n\n⚠️ {failedCount} cirugia(s) no se pudieron sincronizar. Verifica los logs para más detalles.";
            }

            await MessageSender.SendWithRetry(chatId, resultMessage, cancellationToken: ct);

            return syncedCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CALENDAR-SYNC] ❌ Error during sync for chat {chatId}: {ex}");
            
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error durante la sincronización del calendario. Por favor, intenta nuevamente.",
                cancellationToken: ct);
            
            return 0;
        }
    }
}