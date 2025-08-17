using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Helpers;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services;
using RegistroCx.Services.UI;
using RegistroCx.Helpers._0Auth;
using RegistroCx.Services.Repositories;
using RegistroCx.Services.Reports;

namespace RegistroCx.Services.Flow;

public class FlowMessageHandler
{
    private readonly IGoogleOAuthService _oauthService;
    private readonly IUserProfileRepository _userRepo;
    private readonly CalendarSyncService _calendarSync;
    private readonly IAppointmentRepository _appointmentRepo;
    private readonly IReportService _reportService;
    private readonly IQuickEditService? _quickEditService;

    public FlowMessageHandler(
        IGoogleOAuthService oauthService, 
        IUserProfileRepository userRepo, 
        CalendarSyncService calendarSync,
        IAppointmentRepository appointmentRepo,
        IReportService reportService,
        IQuickEditService? quickEditService = null)
    {
        _oauthService = oauthService;
        _userRepo = userRepo;
        _calendarSync = calendarSync;
        _appointmentRepo = appointmentRepo;
        _reportService = reportService;
        _quickEditService = quickEditService;
    }
    public async Task<bool> HandleSpecialCommandsAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        var textLower = rawText.Trim().ToLowerInvariant();
        
        // Comandos de reinicio y cancelación (expandidos)
        if (textLower is "/start" or "/reset" or "/reiniciar" or "reiniciar" or "cancelar" or "empezar de nuevo" or
            "cancel" or "borrar" or "eliminar" or "cancelá" or "borrá" or "eliminá" or "empezar otra vez" or
            "nuevo" or "otra cirugia" or "otra cirugía")
        {
            await MessageSender.SendWithRetry(chatId,
                "✨ Perfecto, empezamos de nuevo. Contame los datos de la cirugía que querés agendar.",
                cancellationToken: ct);
            return true;
        }

        // Mensajes de cancelación de cirugías existentes
        if (textLower.Contains("cancela") && (textLower.Contains("cirug") || textLower.Contains("mañana") || textLower.Contains("hoy") || textLower.Contains("ayer")))
        {
            await MessageSender.SendWithRetry(chatId,
                "Para cancelar cirugías ya agendadas, necesitás contactar directamente al centro quirúrgico o a tu coordinador. Yo solo te ayudo a registrar nuevas cirugías.\n\n¿Querés agendar alguna cirugía nueva?",
                cancellationToken: ct);
            return true;
        }

        // Saludos y mensajes sociales (expandidos)
        if (textLower is "hola" or "hello" or "hi" or "buenas" or "buen día" or "buenos días" or "buenas tardes" or "buenas noches" or
            "¿cómo estás?" or "como estas?" or "¿cómo andás?" or "como andas?" or "¿qué tal?" or "que tal?" or
            "hola, ¿cómo estás?" or "hola como estas" or "hola que tal" or "gracias" or "muchas gracias" or "ok gracias" or
            "perfecto" or "genial" or "excelente" or "muy bien" or "está bien" or "todo bien" or
            "chau" or "adiós" or "hasta luego" or "nos vemos" or "bye")
        {
            var response = textLower.Contains("grac") || textLower.Contains("perfecto") || textLower.Contains("genial") || textLower.Contains("excelente") ?
                "¡De nada! 😊 Cualquier cosa que necesites, acá estoy. ¿Tenés alguna cirugía más para agendar?" :
                textLower.Contains("chau") || textLower.Contains("adiós") || textLower.Contains("hasta") || textLower.Contains("bye") ?
                "¡Hasta luego! 👋 Que tengas un buen día. Cuando necesites agendar cirugías, ya sabés dónde encontrarme." :
                "¡Hola! Muy bien, gracias 😊\n\nSoy tu asistente para registrar cirugías. Solo escribime los datos como:\n\n📝 <b>Ejemplo:</b> \"mañana 2 cers quiroga callao 14hs\"\n\n¿Tenés alguna cirugía para agendar?";
            
            await MessageSender.SendWithRetry(chatId, response, cancellationToken: ct);
            return true;
        }

        // Preguntas sobre funcionamiento
        if (textLower.Contains("¿cómo funciona") || textLower.Contains("como funciona") || 
            textLower.Contains("¿qué hac") || textLower.Contains("que hac") ||
            textLower.Contains("ayuda") || textLower.Contains("/help") ||
            textLower.Contains("¿para qué") || textLower.Contains("para que") ||
            textLower.Contains("explicame") || textLower.Contains("explícame"))
        {
            await MessageSender.SendWithRetry(chatId,
                "📋 <b>¿CÓMO FUNCIONA?</b>\n" +
                "Simplemente escribime los datos de tu cirugía en lenguaje natural. Yo entiendo y organizo automáticamente:\n\n" +
                "🔹 <b>Ejemplo:</b> \"23/08 2 CERS + 1 MLD Sanchez Sanatorio Anchorena Pedro 14hs\"\n" +
                "• Detectaré que son 3 cirugías diferentes\n" +
                "• Extraeré fecha, hora, lugar, cirujano, etc.\n" +
                "• Te pediré solo los datos que falten\n" +
                "• Crearé eventos en tu Google Calendar\n\n" +
                "✨ <b>CARACTERÍSTICAS:</b>\n" +
                "• 🎤 Acepto mensajes de voz\n" +
                "• 🔢 Proceso múltiples cirugías de una vez\n" +
                "• 📅 Sincronización automática con Google Calendar\n" +
                "• 💉 Invito anestesiólogos por email\n\n" +
                "🚀 <b>¡Empezá ahora!</b> Mandame cualquier cirugía y yo me encargo del resto.",
                cancellationToken: ct);
            return true;
        }

        if (textLower is "/autorizar" or "autorizar")
        {
            await HandleAuthorizationCommand(bot, chatId, ct);
            return true;
        }

        if (textLower is "/test-reminder" or "/testreminder")
        {
            await HandleTestReminderCommand(bot, chatId, ct);
            return true;
        }

        if (textLower is "/help-audio" or "/audio-help")
        {
            await HandleAudioHelpCommand(bot, chatId, ct);
            return true;
        }

        if (textLower is "/test-multi" or "/multi-test")
        {
            await HandleTestMultiSurgeriesCommand(bot, chatId, ct);
            return true;
        }

        // Comandos de reportes
        if (textLower is "/semanal" or "/mensual" or "/anual")
        {
            return await _reportService.HandleReportCommandAsync(bot, chatId, rawText, ct);
        }

        // También manejar respuestas a comandos de reportes pendientes (sin /)
        if (await _reportService.HandleReportCommandAsync(bot, chatId, rawText, ct))
        {
            return true;
        }

        return false;
    }

    private async Task HandleAuthorizationCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[AUTHORIZATION] Starting authorization flow for chat {chatId}");

            // Obtener o crear perfil del usuario
            var userProfile = await _userRepo.GetOrCreateAsync(chatId, ct);

            // Construir URL de autorización (reutilizar lógica existente del onboarding)
            var authUrl = _oauthService.BuildAuthUrl(chatId, userProfile.GoogleEmail ?? "user@example.com");

            await MessageSender.SendWithRetry(chatId,
                "🔐 *Autorización de Google Calendar*\n\n" +
                "Para poder crear eventos en tu calendario, necesito que me autorices el acceso a Google Calendar.\n\n" +
                $"🔗 [HACER CLIC AQUÍ PARA AUTORIZAR]({authUrl})\n\n" +
                "Una vez que completes la autorización, te avisaré y sincronizaré automáticamente todas tus cirugías pendientes.\n\n" +
                "⚠️ _El enlace te llevará a una página segura de Google para autorizar el acceso._",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTHORIZATION] Error starting authorization: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error al iniciar el proceso de autorización. Por favor, intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private async Task HandleTestReminderCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[TEST-REMINDER] Manual reminder check requested for chat {chatId}");

            // Get appointments that need reminders (normally would be called by background service)
            var appointmentsNeedingReminders = await _appointmentRepo.GetAppointmentsNeedingRemindersAsync(ct);
            
            if (appointmentsNeedingReminders.Count == 0)
            {
                await MessageSender.SendWithRetry(chatId,
                    "ℹ️ No hay cirugías que necesiten recordatorio en las próximas 24 horas.",
                    cancellationToken: ct);
                return;
            }

            await MessageSender.SendWithRetry(chatId,
                $"🔍 Encontré {appointmentsNeedingReminders.Count} cirugia(s) que necesitan recordatorio:",
                cancellationToken: ct);

            foreach (var appointment in appointmentsNeedingReminders)
            {
                // Test the reminder logic
                if (appointment.FechaHora.HasValue)
                {
                    var timeUntilSurgery = appointment.FechaHora.Value - DateTime.Now;
                    var hoursUntil = (int)timeUntilSurgery.TotalHours;

                    var testMessage = $"⏰ **TEST RECORDATORIO**\n\n" +
                                     $"🏥 **{appointment.Cantidad} {appointment.Cirugia?.ToUpper()}**\n" +
                                     $"📅 **Fecha:** {appointment.FechaHora:dddd, dd MMMM yyyy}\n" +
                                     $"⌚ **Hora:** {appointment.FechaHora:HH:mm}\n" +
                                     $"📍 **Lugar:** {appointment.Lugar}\n" +
                                     $"👨‍⚕️ **Cirujano:** {appointment.Cirujano}\n" +
                                     $"💉 **Anestesiólogo:** {appointment.Anestesiologo}\n\n" +
                                     $"⏳ **Faltan {hoursUntil} horas**\n\n" +
                                     $"*Este es un recordatorio de prueba. El recordatorio real se enviará automáticamente.*";

                    await MessageSender.SendWithRetry(chatId, testMessage, cancellationToken: ct);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TEST-REMINDER] Error testing reminders: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error al probar los recordatorios. Verifica los logs.",
                cancellationToken: ct);
        }
    }

    private async Task HandleAudioHelpCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            var helpMessage = "🎤 **AYUDA DE MENSAJES DE VOZ Y AUDIO**\n\n" +
                             "✨ **Ahora puedes enviar mensajes de voz para registrar cirugías!**\n\n" +
                             "📋 **Cómo funciona:**\n" +
                             "• Mantén presionado el botón del micrófono 🎤\n" +
                             "• Habla claramente describiendo la cirugía\n" +
                             "• Suelta para enviar el mensaje de voz\n" +
                             "• El bot convertirá tu voz a texto y procesará la información\n\n" +
                             "🎯 **Ejemplo de lo que puedes decir:**\n" +
                             "\"Hola, necesito agendar una amigdalectomía para mañana a las 2 de la tarde en el Hospital Italiano con el doctor Rodriguez y anestesiólogo Martinez\"\n\n" +
                             "🔧 **También acepta:**\n" +
                             "• Archivos de audio (.mp3, .ogg, .wav)\n" +
                             "• Mensajes de voz de Telegram\n" +
                             "• Comandos por voz (ej: \"cancelar\", \"confirmar\")\n\n" +
                             "⚡ **El procesamiento toma unos segundos**\n" +
                             "Te mostraré lo que entendí antes de procesarlo.\n\n" +
                             "¡Prueba enviando un mensaje de voz ahora! 🎤";

            await MessageSender.SendWithRetry(chatId, helpMessage, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUDIO-HELP] Error showing audio help: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error mostrando la ayuda de audio.",
                cancellationToken: ct);
        }
    }

    private async Task HandleTestMultiSurgeriesCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            var helpMessage = "🔥 **PRUEBA DE MÚLTIPLES CIRUGÍAS**\n\n" +
                             "✨ **Ahora puedes registrar múltiples cirugías en un solo mensaje!**\n\n" +
                             "📋 **Formatos soportados:**\n" +
                             "• `2 CERS + 1 MLD mañana 14hs Anchorena Quiroga Martinez`\n" +
                             "• `3 adenoides y 2 amígdalas 15/08 Hospital Rodriguez`\n" +
                             "• `CERS x2, MLD x1 hoy 16hs`\n" +
                             "• `2x CERS, 1x MLD pasado mañana`\n" +
                             "• `CERS por 2 más MLD por 1`\n\n" +
                             "🎯 **Ejemplos que puedes probar:**\n" +
                             "1. `2 CERS + 1 MLD mañana 14hs Sanatorio Anchorena Dr. Quiroga Martinez`\n" +
                             "2. `3 adenoides y 2 amígdalas 15/08/2025 Hospital Italiano`\n" +
                             "3. `CERS x2, MLD x1 hoy 16:30 Mater Dei`\n\n" +
                             "⚡ **Cómo funciona:**\n" +
                             "1. **Completa todos los datos** (fecha, hora, lugar, cirujano, anestesiólogo)\n" +
                             "2. **Detecta múltiples cirugías** automáticamente al confirmar\n" +
                             "3. **Crea cada cirugía** con los mismos datos base\n" +
                             "4. **Permite editar individualmente** cada cirugía\n" +
                             "5. **Te muestra resumen final** para confirmar todas juntas\n\n" +
                             "✏️ **Comandos de edición granular:**\n" +
                             "• `cirugía 1 hora 16hs` - Cambiar hora específica\n" +
                             "• `MLD lugar Hospital Italiano` - Cambiar por nombre\n" +
                             "• `primera cirugía anestesiólogo López` - Por posición\n\n" +
                             "🧪 **¡Prueba enviando uno de los ejemplos ahora!**";

            await MessageSender.SendWithRetry(chatId, helpMessage, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-TEST] Error showing multi surgery help: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error mostrando la ayuda de múltiples cirugías.",
                cancellationToken: ct);
        }
    }

    public async Task<bool> HandleConfirmationAsync(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        if (!appt.ConfirmacionPendiente) return false;

        var lower = rawText.Trim().ToLowerInvariant();

        if (lower is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar")
        {
            appt.ConfirmacionPendiente = false;

            var (okFecha, err) = FechasHelper.ValidarFechaCirugia(appt.FechaHora, DateTime.Now);
            if (!okFecha)
            {
                await MessageSender.SendWithRetry(chatId, err!, cancellationToken: ct);
                return true;
            }

            // Nota: El mensaje de confirmación se envía desde AppointmentConfirmationService
            // await MessageSender.SendWithRetry(chatId,
            //     "✅ Confirmado. El evento fue creado en el calendario.",
            //     cancellationToken: ct);
            return true;
        }
        else if (lower.StartsWith("no") || lower.Contains("cambiar") || lower.Contains("correg"))
        {
            appt.ConfirmacionPendiente = false;
            appt.CampoAEditar = Appointment.CampoPendiente.EsperandoNombreCampo;
            await  MessageSender.SendWithRetry(chatId, GetEditModeInstructions(), cancellationToken: ct);
            return true;
        }

        return false;
    }

    public async Task<bool> HandleEditMode(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct, FlowLLMProcessor llmProcessor)
    {
        if (appt.CampoAEditar != Appointment.CampoPendiente.EsperandoNombreCampo) return false;

        // Intentar parsear campo + valor directo
        if (CamposExistentes.TryParseCambioCampo(rawText, out var campoCambio, out var valorDirecto))
        {
            if (!string.IsNullOrWhiteSpace(valorDirecto))
            {
                // NUEVO: Verificar si necesita normalización LLM
                if (campoCambio is Appointment.CampoPendiente.Lugar or 
                                  Appointment.CampoPendiente.Cirujano or 
                                  Appointment.CampoPendiente.Anestesiologo or
                                  Appointment.CampoPendiente.Cirugia)
                {
                    appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                    await llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                    return true;
                }
                else
                {
                    // Para campos que NO necesitan normalización
                    if (CamposExistentes.TryAplicarValorCampo(appt, campoCambio, valorDirecto, out var err))
                    {
                        appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                        await MessageSender.SendWithRetry(chatId,
                            CamposExistentes.GenerarMensajeActualizacion(campoCambio, valorDirecto),
                            cancellationToken: ct);
                        
                        await TryConfirmation(bot, appt, chatId, ct);
                        return true;
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId, err!, cancellationToken: ct);
                        return true;
                    }
                }
            }
            else
            {
                appt.CampoQueFalta = campoCambio;
                appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                appt.IntentosCampoActual = 0;
                await MessageSender.SendWithRetry(chatId,
                    $"Perfecto. Decime el nuevo valor para {CamposExistentes.NombreHumanoCampo(campoCambio)}:",
                    cancellationToken: ct);
                return true;
            }
        }
        
        // Intentar parsear solo el nombre del campo
        if (CamposExistentes.TryParseSoloCampo(rawText, out var soloNombreCampo))
        {
            appt.CampoQueFalta = soloNombreCampo;
            appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
            appt.IntentosCampoActual = 0;
            await MessageSender.SendWithRetry(chatId,
                $"Perfecto. Decime el nuevo valor para {CamposExistentes.NombreHumanoCampo(soloNombreCampo)}:",
                cancellationToken: ct);
            return true;
        }

        await MessageSender.SendWithRetry(chatId,
            "No entendí qué campo querés cambiar. Decí alguno de estos:\n" +
            "'fecha', 'hora', 'lugar', 'cirujano', 'cirugia', 'cantidad', 'anestesiologo'",
            cancellationToken: ct);
        return true;
    }

    public async Task<bool> HandleDirectChanges(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct, FlowLLMProcessor llmProcessor)
    {
        if (!CamposExistentes.TryParseCambioCampo(rawText, out var campoCambioDirecto, out var valorCambioDirecto) ||
            campoCambioDirecto == Appointment.CampoPendiente.Ninguno)
            return false;

        if (!string.IsNullOrWhiteSpace(valorCambioDirecto))
        {
            // Si es lugar, cirujano o anestesiologo, enviar al LLM para normalizar
            if (campoCambioDirecto is Appointment.CampoPendiente.Lugar or 
                                     Appointment.CampoPendiente.Cirujano or 
                                     Appointment.CampoPendiente.Anestesiologo or
                                     Appointment.CampoPendiente.Cirugia) // AGREGADO: cirugia también necesita normalización
            {
                await llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                return true;
            }
            else
            {
                // CORREGIDO: Solo para campos que NO necesitan normalización (cantidad, fecha/hora)
                if (CamposExistentes.TryAplicarValorCampo(appt, campoCambioDirecto, valorCambioDirecto, out var err))
                {
                    await MessageSender.SendWithRetry(chatId,
                        CamposExistentes.GenerarMensajeActualizacion(campoCambioDirecto, valorCambioDirecto),
                        cancellationToken: ct);
                    
                    await TryConfirmation(bot, appt, chatId, ct);
                    return true;
                }
                else
                {
                    await MessageSender.SendWithRetry(chatId, err!, cancellationToken: ct);
                    return true;
                }
            }
        }

        // Si no hay valor, pedir el valor del campo
        appt.CampoQueFalta = campoCambioDirecto;
        appt.IntentosCampoActual = 0;
        await MessageSender.SendWithRetry(chatId,
            $"Necesito el valor de {CamposExistentes.NombreHumanoCampo(campoCambioDirecto)}.",
            cancellationToken: ct);
        return true;
    }

    private string GetEditModeInstructions()
{
    return "✏️ ¿Qué querés cambiar?\n\n" +
           "Podés escribir:\n" +
           "👉 Solo el nombre del campo (ej: `fecha`, `hora`, `lugar`)\n" +
           "👉 O el campo + el nuevo valor (ej: `fecha 08/08`, `cirujano Rodriguez`)\n\n" +
           "Campos disponibles: `fecha`, `hora`, `lugar`, `cirujano`, `cirugia`, `cantidad`, `anestesiologo`";
}

    private async Task TryConfirmation(ITelegramBotClient bot, Appointment appt, long chatId, CancellationToken ct)
    {
        if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct, _quickEditService))
            return;
        
        await MessageSender.SendWithRetry(chatId,
            "Podés cambiar otro campo o escribir 'confirmar'.",
            cancellationToken: ct);
    }
}