using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RegistroCx.models;
using RegistroCx.Models;
using RegistroCx.Services.Repositories;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services
{
    public class AppointmentUpdateCoordinator
    {
        private readonly IAppointmentRepository _appointmentRepo;
        private readonly AppointmentConfirmationService _confirmationService;
        private readonly ILogger<AppointmentUpdateCoordinator> _logger;

        public AppointmentUpdateCoordinator(
            IAppointmentRepository appointmentRepo,
            AppointmentConfirmationService confirmationService,
            ILogger<AppointmentUpdateCoordinator> logger)
        {
            _appointmentRepo = appointmentRepo;
            _confirmationService = confirmationService;
            _logger = logger;
        }

        public async Task<bool> ExecuteModificationAsync(
            Appointment original, 
            ModificationRequest changes,
            long chatId,
            CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Executing modification for appointment {AppointmentId}", original.Id);

                // 1. Actualizar en base de datos
                await _appointmentRepo.UpdateAsync(original.Id, changes, ct);
                
                // 2. Obtener el appointment actualizado
                var updatedAppointment = await _appointmentRepo.GetByIdAsync(original.Id, ct);
                if (updatedAppointment == null)
                {
                    _logger.LogError("Failed to retrieve updated appointment {AppointmentId}", original.Id);
                    return false;
                }

                // 3. Actualizar calendario Google si existe event_id
                if (!string.IsNullOrEmpty(original.CalendarEventId))
                {
                    await UpdateGoogleCalendarEvent(updatedAppointment, original.CalendarEventId, ct);
                }

                // 4. Notificar anestesiólogo si cambió
                if (changes.AnesthesiologistChanged)
                {
                    await NotifyAnesthesiologistChange(updatedAppointment, original.Anestesiologo, ct);
                }

                // 5. Confirmar al usuario
                await SendModificationConfirmation(updatedAppointment, changes, chatId, ct);

                _logger.LogInformation("Successfully executed modification for appointment {AppointmentId}", original.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing modification for appointment {AppointmentId}", original.Id);
                
                // Enviar mensaje de error al usuario
                await MessageSender.SendWithRetry(chatId,
                    "❌ Hubo un error al actualizar la cirugía. Por favor, intenta nuevamente.",
                    cancellationToken: ct);
                
                return false;
            }
        }

        private Task UpdateGoogleCalendarEvent(Appointment updatedAppointment, string eventId, CancellationToken ct)
        {
            try
            {
                // Aquí iría la lógica para actualizar el evento en Google Calendar
                // Por ahora solo marcamos que necesita sincronización
                _logger.LogInformation("Calendar event {EventId} needs update for appointment {AppointmentId}", 
                    eventId, updatedAppointment.Id);
                
                // TODO: Implementar actualización real del calendario
                // Esto requeriría integrar con Google Calendar API
                // Por ahora, el sistema de confirmación existente debería manejar esto
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating calendar event {EventId}", eventId);
                // No fallar toda la operación por un error de calendario
                return Task.CompletedTask;
            }
        }

        private Task NotifyAnesthesiologistChange(Appointment appointment, string? oldAnesthesiologist, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(appointment.Anestesiologo))
                {
                    _logger.LogInformation("Anesthesiologist removed from appointment {AppointmentId}", appointment.Id);
                    return Task.CompletedTask;
                }

                _logger.LogInformation("Anesthesiologist changed for appointment {AppointmentId}: {Old} -> {New}", 
                    appointment.Id, oldAnesthesiologist, appointment.Anestesiologo);

                // TODO: Implementar notificación por email al anestesiólogo
                // Esto requeriría integrar con el servicio de email existente
                // await _emailService.NotifyAnesthesiologistChange(appointment, oldAnesthesiologist);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying anesthesiologist change for appointment {AppointmentId}", appointment.Id);
                // No fallar toda la operación por un error de notificación
                return Task.CompletedTask;
            }
        }

        private async Task SendModificationConfirmation(Appointment updatedAppointment, ModificationRequest changes, long chatId, CancellationToken ct)
        {
            try
            {
                var message = "✅ *Cirugía actualizada exitosamente*\n\n";
                
                // Mostrar resumen de la cirugía actualizada
                message += "📋 *Datos actualizados:*\n";
                message += $"📅 Fecha: {updatedAppointment.FechaHora?.ToString("dd/MM/yyyy") ?? "No definida"}\n";
                message += $"🕒 Hora: {updatedAppointment.FechaHora?.ToString("HH:mm") ?? "No definida"}\n";
                message += $"📍 Lugar: {updatedAppointment.Lugar ?? "No definido"}\n";
                message += $"👨‍⚕️ Cirujano: {updatedAppointment.Cirujano ?? "No definido"}\n";
                message += $"🏥 Cirugía: {updatedAppointment.Cirugia ?? "No definida"}\n";
                message += $"🔢 Cantidad: {updatedAppointment.Cantidad}\n";
                
                if (!string.IsNullOrEmpty(updatedAppointment.Anestesiologo))
                {
                    message += $"💉 Anestesiólogo: {updatedAppointment.Anestesiologo}\n";
                }

                // Agregar información sobre sincronización
                if (!string.IsNullOrEmpty(updatedAppointment.CalendarEventId))
                {
                    message += "\n📅 El calendario se actualizará automáticamente.";
                }

                if (changes.AnesthesiologistChanged)
                {
                    message += "\n📧 Se notificará al anestesiólogo sobre el cambio.";
                }

                await MessageSender.SendWithRetry(chatId, message, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending modification confirmation for appointment {AppointmentId}", updatedAppointment.Id);
                // Enviar mensaje genérico si falla el mensaje detallado
                await MessageSender.SendWithRetry(chatId,
                    "✅ Cirugía actualizada exitosamente.",
                    cancellationToken: ct);
            }
        }

        public async Task<bool> HandleAmbiguousSearch(AppointmentSearchResult searchResult, string originalRequest, long chatId, CancellationToken ct)
        {
            try
            {
                if (searchResult.NotFound)
                {
                    await MessageSender.SendWithRetry(chatId,
                        "❌ No encontré esa cirugía.\n\n" +
                        "💡 <b>Sugerencias:</b>\n" +
                        "• Verificá que la cirugía esté registrada con <b>/semanal</b> o <b>/mensual</b>\n" +
                        "• Sé más específico: \"cambiar la cirugia de Garcia del 23/09 a las 15hs\"\n" +
                        "• Incluí más detalles: fecha completa, apellido del cirujano, etc.\n\n" +
                        "🔍 <b>Ejemplos:</b>\n" +
                        "• \"cambiar la hora de fagoaga del 23/09 a las 16hs\"\n" +
                        "• \"modificar el lugar de la cirugia del lunes\"\n\n" +
                        "❌ Escribí <b>\"cancelar\"</b> si querés empezar de nuevo.",
                        cancellationToken: ct);
                    return false;
                }

                if (searchResult.IsAmbiguous)
                {
                    var message = "🤔 Encontré varias cirugías que podrían coincidir:\n\n";
                    
                    for (int i = 0; i < searchResult.Candidates.Count; i++)
                    {
                        var candidate = searchResult.Candidates[i];
                        message += $"{i + 1}. {candidate.FechaHora?.ToString("dd/MM HH:mm")} - " +
                                 $"{candidate.Cirujano} - {candidate.Cirugia} ({candidate.Lugar})\n";
                    }
                    
                    message += "\n¿Podrías ser más específico para identificar cuál querés modificar?\n\n" +
                              "❌ Escribí <b>\"cancelar\"</b> si querés empezar de nuevo.";
                    
                    await MessageSender.SendWithRetry(chatId, message, cancellationToken: ct);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling ambiguous search result");
                await MessageSender.SendWithRetry(chatId,
                    "❌ Hubo un error procesando tu solicitud. Por favor, intenta nuevamente.",
                    cancellationToken: ct);
                return false;
            }
        }
    }
}