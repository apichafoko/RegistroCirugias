using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
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
    private readonly UserLearningService? _learningService;
    private readonly EquipoService _equipoService;

    public AppointmentConfirmationService(
        IUserProfileRepository userRepo,
        IAppointmentRepository appointmentRepo,
        IAnesthesiologistRepository anesthesiologistRepo,
        IGoogleOAuthService googleOAuth,
        IGoogleCalendarService calendarService,
        UserLearningService? learningService = null,
        EquipoService? equipoService = null)
    {
        _userRepo = userRepo;
        _appointmentRepo = appointmentRepo;
        _anesthesiologistRepo = anesthesiologistRepo;
        _googleOAuth = googleOAuth;
        _calendarService = calendarService;
        _learningService = learningService;
        _equipoService = equipoService ?? throw new ArgumentNullException(nameof(equipoService));
    }

    public async Task<bool> ProcessConfirmationAsync(
        ITelegramBotClient bot, 
        Appointment appt, 
        long chatId, 
        CancellationToken ct,
        bool silent = false)
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

            // APRENDER después de confirmación exitosa
            await LearnFromConfirmedAppointment(appt, chatId, ct);

            // 3. Manejar invitación del anestesiólogo (solo si hay anestesiólogo asignado)
            if (!string.IsNullOrEmpty(appt.Anestesiologo))
            {
                await HandleAnesthesiologistInvitation(bot, appt, chatId, eventId, ct, silent);
            }
            else if (!silent)
            {
                Console.WriteLine("[CONFIRMATION] No anesthesiologist assigned, skipping invitation");
            }

            // Solo enviar mensaje de confirmación individual si no está en modo silencioso
            if (!silent)
            {
                await MessageSender.SendWithRetry(chatId,
                    "✅ Confirmado. El evento fue creado en el calendario con recordatorio de 24 horas y guardado en la base de datos.",
                    cancellationToken: ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIRMATION] ❌ Error in atomic transaction: {ex}");
            
            // Para errores de OAuth, mantener el appointment pero informar del problema
            if (ex.Message.Contains("vuelve a autorizar"))
            {
                Console.WriteLine($"[CONFIRMATION] OAuth error - keeping appointment {appointmentId} but unable to create calendar event");
                
                if (!silent)
                {
                    await MessageSender.SendWithRetry(chatId,
                        "✅ Appointment confirmado y guardado en la base de datos.\n\n" +
                        "⚠️ No se pudo crear el evento en Google Calendar porque tu autorización expiró.\n\n" +
                        "Para crear eventos de calendario en el futuro, escribe /autorizar para renovar el acceso.",
                        cancellationToken: ct);
                }
                
                return true; // El appointment se guardó exitosamente
            }
            
            // Para otros errores, hacer rollback completo
            await RollbackTransaction(appointmentId > 0 ? appointmentId : null, eventId, chatId, ct);
            
            if (!silent)
            {
                // Crear teclado con opciones de reintento y cancelar
                var retryKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("🔄 Reintentar", $"confirm_{appt.Id}"),
                        InlineKeyboardButton.WithCallbackData("❌ Cancelar", $"cancel_{appt.Id}")
                    }
                });

                await MessageSender.SendWithRetry(chatId,
                    "❌ Hubo un error al procesar la confirmación. Por favor, intenta nuevamente.",
                    replyMarkup: retryKeyboard,
                    cancellationToken: ct);
            }
            return false;
        }
    }

    private async Task<long> SaveAppointmentToDatabase(Appointment appt, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[DB] Saving appointment for chat {chatId}: {appt.Cirugia} on {appt.FechaHora}");
            
            // Obtener el GoogleEmail y user_profile_id del usuario 
            var userProfile = await _userRepo.GetOrCreateAsync(chatId, ct);
            appt.GoogleEmail = userProfile.GoogleEmail;
            appt.UserProfileId = userProfile.Id;
            
            // Resolver chatId a equipoId para el nuevo sistema de equipos
            var equipoId = await _equipoService.ObtenerPrimerEquipoIdPorChatIdAsync(chatId, ct);
            
            var appointmentId = await _appointmentRepo.SaveAsync(appt, equipoId, ct);
            
            Console.WriteLine($"[DB] ✅ Appointment saved with ID: {appointmentId} (EquipoId: {equipoId}, GoogleEmail: {appt.GoogleEmail})");
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

    private async Task HandleAnesthesiologistInvitation(ITelegramBotClient bot, Appointment appt, long chatId, string eventId, CancellationToken ct, bool silent = false)
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
                
                // Solo enviar mensajes individuales si no está en modo silencioso
                if (!silent)
                {
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
            }
            else
            {
                // 3. Si no tiene email, marcarlo para solicitud posterior (no pedirlo inmediatamente en modo silencioso)
                if (!silent)
                {
                    await RequestAnesthesiologistEmail(bot, appt, chatId, eventId, ct);
                }
                else
                {
                    // En modo silencioso, solo marcar que necesita email
                    appt.CampoQueFalta = Appointment.CampoPendiente.EsperandoEmailAnestesiologo;
                    appt.PendingEventId = eventId;
                    appt.PendingAnesthesiologistName = appt.Anestesiologo;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST] Error handling invitation: {ex}");
            // No lanzar error aquí, la confirmación principal debe continuar
            if (!silent)
            {
                await MessageSender.SendWithRetry(chatId,
                    $"⚠️ Hubo un problema al enviar la invitación a {appt.Anestesiologo}. El evento está creado pero podés compartirlo manualmente.",
                    cancellationToken: ct);
            }
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
        
        // IMPORTANTE: Marcar que el appointment debe ser limpiado
        appt.ReadyForCleanup = true;
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

    /// <summary>
    /// Aprende de un appointment confirmado exitosamente por el usuario
    /// </summary>
    private async Task LearnFromConfirmedAppointment(Appointment appt, long chatId, CancellationToken ct)
    {
        if (_learningService == null)
        {
            Console.WriteLine("[LEARNING] Learning service not available, skipping learning from confirmed appointment");
            return;
        }

        try
        {
            // Construir diccionario con los datos confirmados del appointment
            var confirmedData = new Dictionary<string, string>();

            // Agregar datos solo si están presentes y no son valores por defecto
            if (!string.IsNullOrWhiteSpace(appt.Lugar))
                confirmedData["lugar"] = appt.Lugar;
            
            if (!string.IsNullOrWhiteSpace(appt.Cirujano))
                confirmedData["cirujano"] = appt.Cirujano;
            
            if (!string.IsNullOrWhiteSpace(appt.Cirugia))
                confirmedData["cirugia"] = appt.Cirugia;
            
            if (!string.IsNullOrWhiteSpace(appt.Anestesiologo))
                confirmedData["anestesiologo"] = appt.Anestesiologo;

            if (appt.Cantidad.HasValue && appt.Cantidad > 0)
                confirmedData["cantidad"] = appt.Cantidad.Value.ToString();

            // Solo aprender si hay datos útiles y hay inputs históricos
            if (confirmedData.Any() && appt.HistoricoInputs.Any())
            {
                // Usar el input original (primer mensaje del usuario)
                var originalInput = appt.HistoricoInputs.First();
                
                Console.WriteLine($"[LEARNING] Learning from confirmed appointment for user {chatId}: {originalInput}");
                Console.WriteLine($"[LEARNING] Confirmed data: {string.Join(", ", confirmedData.Select(kv => $"{kv.Key}={kv.Value}"))}");
                
                await _learningService.LearnFromInteraction(chatId, originalInput, confirmedData, ct);
                
                Console.WriteLine("[LEARNING] ✅ Successfully learned from confirmed appointment");
            }
            else
            {
                Console.WriteLine("[LEARNING] No useful data or input history to learn from");
            }
        }
        catch (Exception ex)
        {
            // No fallar la confirmación por errores de aprendizaje
            Console.WriteLine($"[LEARNING] ⚠️ Error learning from confirmed appointment: {ex.Message}");
        }
    }
}