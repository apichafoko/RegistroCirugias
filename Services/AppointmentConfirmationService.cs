using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Services.Repositories;
using RegistroCx.Helpers._0Auth;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services;

public class AppointmentConfirmationService
{
    private readonly IUserProfileRepository _userRepo;
    private readonly IAppointmentRepository _appointmentRepo;
    private readonly IAnesthesiologistRepository _anesthesiologistRepo;
    private readonly IGoogleOAuthService _googleOAuth;
    private readonly IGoogleCalendarService _calendarService;

    public AppointmentConfirmationService(
        IUserProfileRepository userRepo,
        IAppointmentRepository appointmentRepo,
        IAnesthesiologistRepository anesthesiologistRepo,
        IGoogleOAuthService googleOAuth,
        IGoogleCalendarService calendarService)
    {
        _userRepo = userRepo;
        _appointmentRepo = appointmentRepo;
        _anesthesiologistRepo = anesthesiologistRepo;
        _googleOAuth = googleOAuth;
        _calendarService = calendarService;
    }

    public async Task<bool> ProcessConfirmationAsync(
        ITelegramBotClient bot, 
        Appointment appt, 
        long chatId, 
        CancellationToken ct)
    {
        long appointmentId = 0;
        string? eventId = null;
        
        try
        {
            Console.WriteLine($"[CONFIRMATION] Starting atomic transaction for chat {chatId}");
            
            // 1. Guardar en base de datos
            appointmentId = await SaveAppointmentToDatabase(appt, chatId, ct);

            // 2. Crear evento en Google Calendar
            eventId = await CreateCalendarEvent(appt, chatId, ct);

            // 3. Actualizar el appointment con el calendar_event_id
            await _appointmentRepo.UpdateCalendarEventAsync(appointmentId, eventId, ct);

            Console.WriteLine($"[CONFIRMATION] ✅ Atomic transaction successful - DB ID: {appointmentId}, Calendar ID: {eventId}");

            // 3. Manejar invitación del anestesiólogo (no crítico, puede fallar)
            await HandleAnesthesiologistInvitation(bot, appt, chatId, eventId, ct);

            await MessageSender.SendWithRetry(chatId,
                "✅ Confirmado. El evento fue creado en el calendario con recordatorio de 24 horas y guardado en la base de datos.",
                cancellationToken: ct);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIRMATION] ❌ Error in atomic transaction: {ex}");
            
            // Para errores de OAuth, mantener el appointment pero informar del problema
            if (ex.Message.Contains("vuelve a autorizar"))
            {
                Console.WriteLine($"[CONFIRMATION] OAuth error - keeping appointment {appointmentId} but unable to create calendar event");
                
                await MessageSender.SendWithRetry(chatId,
                    "✅ Appointment confirmado y guardado en la base de datos.\n\n" +
                    "⚠️ No se pudo crear el evento en Google Calendar porque tu autorización expiró.\n\n" +
                    "Para crear eventos de calendario en el futuro, escribe /autorizar para renovar el acceso.",
                    cancellationToken: ct);
                
                return true; // El appointment se guardó exitosamente
            }
            
            // Para otros errores, hacer rollback completo
            await RollbackTransaction(appointmentId > 0 ? appointmentId : null, eventId, chatId, ct);
            
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error al procesar la confirmación. Por favor, intenta nuevamente.",
                cancellationToken: ct);
            return false;
        }
    }

    private async Task<long> SaveAppointmentToDatabase(Appointment appt, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[DB] Saving appointment for chat {chatId}: {appt.Cirugia} on {appt.FechaHora}");
            
            var appointmentId = await _appointmentRepo.SaveAsync(appt, chatId, ct);
            
            Console.WriteLine($"[DB] ✅ Appointment saved with ID: {appointmentId}");
            return appointmentId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] ❌ Error saving appointment: {ex.Message}");
            throw;
        }
    }

    private async Task<string> CreateCalendarEvent(Appointment appt, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[CALENDAR] Creating event for chat {chatId}: {appt.Cirugia} on {appt.FechaHora}");
            
            var eventId = await _calendarService.CreateAppointmentEventAsync(appt, chatId, ct);
            
            Console.WriteLine($"[CALENDAR] ✅ Event created successfully with ID: {eventId}");
            return eventId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CALENDAR] ❌ Error creating calendar event: {ex}");
            throw;
        }
    }

    private async Task HandleAnesthesiologistInvitation(ITelegramBotClient bot, Appointment appt, long chatId, string eventId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Handling invitation for: {appt.Anestesiologo}");
            
            // 1. Buscar el email del anestesiólogo en base de datos
            var email = await FindAnesthesiologistEmail(appt.Anestesiologo!, ct);
            
            if (!string.IsNullOrWhiteSpace(email))
            {
                // 2. Si tiene email, enviar invitación del calendario
                var inviteSent = await _calendarService.SendCalendarInviteAsync(eventId, email, chatId, ct);
                
                if (inviteSent)
                {
                    await MessageSender.SendWithRetry(chatId,
                        $"📧 Invitación de calendario enviada a {appt.Anestesiologo} ({email})",
                        cancellationToken: ct);
                }
                else
                {
                    await MessageSender.SendWithRetry(chatId,
                        $"⚠️ No pude enviar la invitación a {appt.Anestesiologo} ({email}). Podés compartir el evento manualmente.",
                        cancellationToken: ct);
                }
            }
            else
            {
                // 3. Si no tiene email, pedírselo al usuario
                await RequestAnesthesiologistEmail(bot, appt, chatId, eventId, ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Error handling invitation: {ex}");
            // No lanzar error aquí, la confirmación principal debe continuar
            await MessageSender.SendWithRetry(chatId,
                $"⚠️ Hubo un problema al enviar la invitación a {appt.Anestesiologo}. El evento está creado pero podés compartirlo manualmente.",
                cancellationToken: ct);
        }
    }

    private async Task<string?> FindAnesthesiologistEmail(string anesthesiologistName, CancellationToken ct)
    {
        try
        {
            // Buscar por nombre completo
            var email = await _anesthesiologistRepo.GetEmailByNameAsync(anesthesiologistName, ct);
            if (!string.IsNullOrWhiteSpace(email))
            {
                Console.WriteLine($"[ANESTHESIOLOGIST] ✅ Found email by name: {email}");
                return email;
            }

            // Buscar por nickname
            email = await _anesthesiologistRepo.GetEmailByNicknameAsync(anesthesiologistName, ct);
            if (!string.IsNullOrWhiteSpace(email))
            {
                Console.WriteLine($"[ANESTHESIOLOGIST] ✅ Found email by nickname: {email}");
                return email;
            }

            Console.WriteLine($"[ANESTHESIOLOGIST] ❌ No email found for: {anesthesiologistName}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Error searching email: {ex}");
            return null;
        }
    }

    private async Task RequestAnesthesiologistEmail(ITelegramBotClient bot, Appointment appt, long chatId, string eventId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Requesting email for: {appt.Anestesiologo}");
            
            await MessageSender.SendWithRetry(chatId,
                $"📧 Para enviar la invitación de calendario a {appt.Anestesiologo}, necesito su email.\n\n" +
                $"Podés:\n" +
                $"• Enviarme el email del anestesiólogo\n" +
                $"• Escribir 'saltar' si no querés enviarlo ahora\n\n" +
                $"El evento ya está creado en tu calendario.",
                cancellationToken: ct);

            // Establecer estado para capturar la respuesta del usuario
            appt.CampoQueFalta = Appointment.CampoPendiente.EsperandoEmailAnestesiologo;
            appt.PendingEventId = eventId;
            appt.PendingAnesthesiologistName = appt.Anestesiologo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Error requesting email: {ex}");
        }
    }

    public async Task<bool> HandleEmailResponse(ITelegramBotClient bot, Appointment appt, string emailOrSkip, long chatId, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(appt.PendingEventId) || string.IsNullOrWhiteSpace(appt.PendingAnesthesiologistName))
            {
                Console.WriteLine("[ANESTHESIOLOGIST] Missing pending email context");
                return false;
            }

            var input = emailOrSkip.Trim().ToLower();
            
            // Si el usuario quiere saltar
            if (input == "saltar" || input == "skip")
            {
                await MessageSender.SendWithRetry(chatId,
                    $"⏭️ Email de invitación omitido. El evento de {appt.PendingAnesthesiologistName} está creado en tu calendario y podés compartirlo manualmente.",
                    cancellationToken: ct);
                
                // Limpiar estado
                ClearEmailCaptureState(appt);
                return true;
            }

            // Verificar si es un email válido
            if (IsValidEmail(emailOrSkip))
            {
                // Enviar invitación
                var inviteSent = await _calendarService.SendCalendarInviteAsync(appt.PendingEventId, emailOrSkip, chatId, ct);
                
                if (inviteSent)
                {
                    await MessageSender.SendWithRetry(chatId,
                        $"📧✅ Invitación de calendario enviada a {appt.PendingAnesthesiologistName} ({emailOrSkip})",
                        cancellationToken: ct);
                    
                    // Guardar el email en la base de datos para futuros usos
                    await SaveAnesthesiologistEmail(appt.PendingAnesthesiologistName, emailOrSkip, ct);
                }
                else
                {
                    await MessageSender.SendWithRetry(chatId,
                        $"❌ No pude enviar la invitación a {emailOrSkip}. Podés compartir el evento manualmente.",
                        cancellationToken: ct);
                }
                
                // Limpiar estado
                ClearEmailCaptureState(appt);
                return true;
            }
            else
            {
                // Email inválido, pedir nuevamente
                await MessageSender.SendWithRetry(chatId,
                    "❌ El formato del email no es válido. Por favor, ingresá un email válido o escribí 'saltar' para omitir.",
                    cancellationToken: ct);
                return false; // No limpiar estado, seguir esperando
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Error handling email response: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando el email. El evento está creado en tu calendario.",
                cancellationToken: ct);
            
            // Limpiar estado en caso de error
            ClearEmailCaptureState(appt);
            return true;
        }
    }

    private void ClearEmailCaptureState(Appointment appt)
    {
        appt.CampoQueFalta = Appointment.CampoPendiente.Ninguno;
        appt.PendingEventId = null;
        appt.PendingAnesthesiologistName = null;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private async Task SaveAnesthesiologistEmail(string anesthesiologistName, string email, CancellationToken ct)
    {
        try
        {
            // Intentar extraer nombre y apellido del nombre completo
            var parts = anesthesiologistName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var nombre = parts.Length > 0 ? parts[0] : anesthesiologistName;
            var apellido = parts.Length > 1 ? string.Join(" ", parts[1..]) : "";

            await _anesthesiologistRepo.SaveAsync(nombre, apellido, email, ct);
            Console.WriteLine($"[ANESTHESIOLOGIST] ✅ Email saved for {anesthesiologistName}: {email}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Warning - couldn't save email: {ex.Message}");
            // No lanzar error, esto es solo para mejorar experiencia futura
        }
    }

    private async Task RollbackTransaction(long? appointmentId, string? eventId, long chatId, CancellationToken ct)
    {
        var rollbackTasks = new List<Task>();
        
        // Rollback calendario si se creó
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            rollbackTasks.Add(Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"[ROLLBACK] Deleting calendar event: {eventId}");
                    await _calendarService.DeleteEventAsync(eventId, chatId, ct);
                    Console.WriteLine($"[ROLLBACK] ✅ Calendar event deleted: {eventId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ROLLBACK] ⚠️ Failed to delete calendar event {eventId}: {ex.Message}");
                }
            }));
        }

        // Rollback BD si se creó  
        if (appointmentId.HasValue)
        {
            rollbackTasks.Add(Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"[ROLLBACK] Deleting appointment: {appointmentId}");
                    await _appointmentRepo.DeleteAsync(appointmentId.Value, ct);
                    Console.WriteLine($"[ROLLBACK] ✅ Appointment deleted: {appointmentId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ROLLBACK] ⚠️ Failed to delete appointment {appointmentId}: {ex.Message}");
                }
            }));
        }

        // Ejecutar rollbacks en paralelo
        if (rollbackTasks.Any())
        {
            try
            {
                await Task.WhenAll(rollbackTasks);
                Console.WriteLine($"[ROLLBACK] ✅ Rollback completed for chat {chatId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ROLLBACK] ⚠️ Some rollback operations failed: {ex.Message}");
            }
        }
    }
}