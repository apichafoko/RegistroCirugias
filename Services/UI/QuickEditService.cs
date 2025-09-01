using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using RegistroCx.Models;
using RegistroCx.Services.Caching;
using RegistroCx.Services;
using RegistroCx.Services.Flow;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services.UI
{
    public class QuickEditService : IQuickEditService
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<QuickEditService> _logger;
        private readonly AppointmentConfirmationService? _confirmationService;
        private readonly Dictionary<long, Appointment>? _pendingAppointments;
        
        // Callback data prefixes
        private const string CONFIRM_PREFIX = "confirm_";
        private const string EDIT_PREFIX = "edit_";
        private const string CANCEL_PREFIX = "cancel_";
        private const string DATE_PREFIX = "date_";
        private const string TIME_PREFIX = "time_";
        private const string SURGEON_PREFIX = "surgeon_";
        private const string LOCATION_PREFIX = "location_";
        private const string BACK_PREFIX = "back_";

        public QuickEditService(ICacheService cacheService, ILogger<QuickEditService> logger, AppointmentConfirmationService? confirmationService = null, Dictionary<long, Appointment>? pendingAppointments = null)
        {
            _cacheService = cacheService;
            _logger = logger;
            _confirmationService = confirmationService;
            _pendingAppointments = pendingAppointments;
        }

        public InlineKeyboardMarkup GenerateConfirmationKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();

            // First row: Main actions
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("✅ Confirmar", $"{CONFIRM_PREFIX}{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("✏️ Editar", $"{EDIT_PREFIX}{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("❌ Cancelar", $"{CANCEL_PREFIX}{appointment.Id}")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public InlineKeyboardMarkup GenerateEditKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();

            // Row 1: Date and Time
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📅 Fecha", $"{DATE_PREFIX}{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("🕒 Hora", $"{TIME_PREFIX}{appointment.Id}")
            });

            // Row 2: Surgeon and Surgery type
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("👨‍⚕️ Cirujano", $"{SURGEON_PREFIX}{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("🏥 Cirugía", $"surgery_{appointment.Id}")
            });

            // Row 3: Location and Anesthesiologist
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📍 Lugar", $"{LOCATION_PREFIX}{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("💉 Anestesiólogo", $"anesthesiologist_{appointment.Id}")
            });

            // Row 4: Quantity and back
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔢 Cantidad", $"quantity_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", $"{BACK_PREFIX}{appointment.Id}")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public InlineKeyboardMarkup GenerateDateSelectionKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            var today = DateTime.Today;

            // Generate next 7 days
            for (int i = 0; i < 7; i++)
            {
                var date = today.AddDays(i);
                var dayName = i == 0 ? "Hoy" : i == 1 ? "Mañana" : date.ToString("dddd");
                var dateText = $"{dayName} {date:dd/MM}";
                
                buttons.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(dateText, $"setdate_{appointment.Id}_{date:yyyy-MM-dd}")
                });
            }

            // Add "Other date" and "Back" options
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📅 Otra fecha", $"otherdate_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", $"{EDIT_PREFIX}{appointment.Id}")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public InlineKeyboardMarkup GenerateTimeSelectionKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Common surgery times (8 AM to 6 PM)
            var commonTimes = new[]
            {
                new { Display = "08:00", Value = "08:00" },
                new { Display = "09:00", Value = "09:00" },
                new { Display = "10:00", Value = "10:00" },
                new { Display = "11:00", Value = "11:00" },
                new { Display = "12:00", Value = "12:00" },
                new { Display = "13:00", Value = "13:00" },
                new { Display = "14:00", Value = "14:00" },
                new { Display = "15:00", Value = "15:00" },
                new { Display = "16:00", Value = "16:00" },
                new { Display = "17:00", Value = "17:00" },
                new { Display = "18:00", Value = "18:00" }
            };

            // Add times in rows of 3
            for (int i = 0; i < commonTimes.Length; i += 3)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 3 && i + j < commonTimes.Length; j++)
                {
                    var time = commonTimes[i + j];
                    row.Add(InlineKeyboardButton.WithCallbackData(time.Display, $"settime_{appointment.Id}_{time.Value}"));
                }
                buttons.Add(row);
            }

            // Add "Other time" and "Back" options
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🕒 Otra hora", $"othertime_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", $"{EDIT_PREFIX}{appointment.Id}")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public async Task<InlineKeyboardMarkup> GenerateSurgeonSelectionKeyboardAsync()
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Get common surgeons from cache
            var commonSurgeons = await _cacheService.GetSurgeonNamesAsync();

            // Add surgeons in rows of 2
            for (int i = 0; i < commonSurgeons.Count; i += 2)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 2 && i + j < commonSurgeons.Count; j++)
                {
                    var surgeon = commonSurgeons[i + j];
                    row.Add(InlineKeyboardButton.WithCallbackData(surgeon, $"setsurgeon_{surgeon}"));
                }
                buttons.Add(row);
            }

            // Add "Other surgeon" option
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("👨‍⚕️ Otro cirujano", "othersurgeon"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", "back_edit")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public async Task<InlineKeyboardMarkup> GenerateLocationSelectionKeyboardAsync()
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Get common locations from cache
            var commonLocations = await _cacheService.GetLocationNamesAsync();

            // Add locations in rows of 1 (locations names can be long)
            foreach (var location in commonLocations)
            {
                buttons.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(location, $"setlocation_{location}")
                });
            }

            // Add "Other location" option
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📍 Otro lugar", "otherlocation"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", "back_edit")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public async Task<bool> HandleCallbackQueryAsync(ITelegramBotClient bot, long chatId, string callbackData, int messageId, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Handling callback query: {CallbackData}", callbackData);

                // Parse callback data
                var parts = callbackData.Split('_', 2);
                if (parts.Length < 2) return false;

                var action = parts[0];
                var parameter = parts[1];

                switch (action)
                {
                    case "confirm":
                        await HandleConfirmAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "edit":
                        await HandleEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "cancel":
                        if (parameter.StartsWith("modification_"))
                        {
                            await HandleCancelModificationAsync(bot, chatId, messageId, ct);
                        }
                        else
                        {
                            await HandleCancelAsync(bot, chatId, messageId, parameter, ct);
                        }
                        return true;
                        
                    case "modify":
                        await HandleModifyFieldAsync(bot, chatId, messageId, callbackData, ct);
                        return true;
                        
                    case "help":
                        await HandleHelpOptionAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "date":
                        await HandleDateEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "time":
                        await HandleTimeEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "surgeon":
                        await HandleSurgeonEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "location":
                        await HandleLocationEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setdate":
                        await HandleSetDateAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "settime":
                        await HandleSetTimeAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setsurgeon":
                        await HandleSetSurgeonAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setlocation":
                        await HandleSetLocationAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "back":
                        await HandleBackAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    default:
                        _logger.LogWarning("Unknown callback action: {Action}", action);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling callback query: {CallbackData}", callbackData);
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando la acción. Intenta nuevamente.", cancellationToken: ct);
                return false;
            }
        }

        public async Task SendAppointmentWithEditButtonsAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string messageText, CancellationToken ct)
        {
            var keyboard = GenerateConfirmationKeyboard(appointment);
            
            await MessageSender.SendWithRetry(
                chatId,
                messageText,
                replyMarkup: keyboard,
                cancellationToken: ct
            );
        }

        public async Task UpdateMessageKeyboardAsync(ITelegramBotClient bot, long chatId, int messageId, InlineKeyboardMarkup newKeyboard, CancellationToken ct)
        {
            try
            {
                await bot.EditMessageReplyMarkup(
                    chatId: chatId,
                    messageId: messageId,
                    replyMarkup: newKeyboard,
                    cancellationToken: ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not update message keyboard for message {MessageId}", messageId);
            }
        }

        // Private handler methods
        private async Task HandleConfirmAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            try
            {
                // Get appointment from pending appointments
                if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
                {
                    // Use the full confirmation service to create calendar event and save to DB
                    if (_confirmationService != null)
                    {
                        await _confirmationService.ProcessConfirmationAsync(bot, appointment, chatId, ct);
                        
                        // Clear from pending after successful confirmation
                        _pendingAppointments.Remove(chatId);
                    }
                    else
                    {
                        // Fallback if confirmation service is not available
                        await MessageSender.SendWithRetry(chatId, "✅ Cirugía confirmada exitosamente.", cancellationToken: ct);
                    }
                }
                else
                {
                    // Error if appointment not found in context - cannot retry without appointment data
                    await MessageSender.SendWithRetry(chatId, "❌ No se puede reintentar la confirmación. Por favor, crea la cirugía nuevamente.", cancellationToken: ct);
                }
                
                // Remove inline keyboard
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming appointment for chat {ChatId}", chatId);
                await MessageSender.SendWithRetry(chatId, "❌ Error confirmando cirugía. Intenta nuevamente.", cancellationToken: ct);
            }
        }

        private async Task HandleEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            // Create a dummy appointment for the example - in real implementation, get from DB
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var editKeyboard = GenerateEditKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, editKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "✏️ Selecciona qué campo quieres editar:", cancellationToken: ct);
        }

        private async Task HandleCancelAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, "❌ Cirugía cancelada.", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleDateEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var dateKeyboard = GenerateDateSelectionKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, dateKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "📅 Selecciona la nueva fecha:", cancellationToken: ct);
        }

        private async Task HandleTimeEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var timeKeyboard = GenerateTimeSelectionKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, timeKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "🕒 Selecciona la nueva hora:", cancellationToken: ct);
        }

        private async Task HandleSurgeonEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var surgeonKeyboard = await GenerateSurgeonSelectionKeyboardAsync();
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, surgeonKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "👨‍⚕️ Selecciona el cirujano:", cancellationToken: ct);
        }

        private async Task HandleLocationEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var locationKeyboard = await GenerateLocationSelectionKeyboardAsync();
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, locationKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "📍 Selecciona el lugar:", cancellationToken: ct);
        }

        private async Task HandleSetDateAsync(ITelegramBotClient bot, long chatId, int messageId, string parameter, CancellationToken ct)
        {
            var parts = parameter.Split('_');
            if (parts.Length >= 2)
            {
                var appointmentId = parts[0];
                var dateStr = parts[1];
                
                if (DateTime.TryParse(dateStr, out var newDate))
                {
                    await MessageSender.SendWithRetry(chatId, $"📅 Fecha actualizada a: {newDate:dd/MM/yyyy}", cancellationToken: ct);
                    
                    // TODO: Update appointment in database
                    // await _appointmentRepo.UpdateDateAsync(long.Parse(appointmentId), newDate, ct);
                    
                    // Remove inline keyboard
                    await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
                }
            }
        }

        private async Task HandleSetTimeAsync(ITelegramBotClient bot, long chatId, int messageId, string parameter, CancellationToken ct)
        {
            var parts = parameter.Split('_');
            if (parts.Length >= 2)
            {
                var appointmentId = parts[0];
                var timeStr = parts[1];
                
                await MessageSender.SendWithRetry(chatId, $"🕒 Hora actualizada a: {timeStr}", cancellationToken: ct);
                
                // TODO: Update appointment in database
                // await _appointmentRepo.UpdateTimeAsync(long.Parse(appointmentId), TimeOnly.Parse(timeStr), ct);
                
                // Remove inline keyboard
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
        }

        private async Task HandleSetSurgeonAsync(ITelegramBotClient bot, long chatId, int messageId, string surgeonName, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, $"👨‍⚕️ Cirujano actualizado a: {surgeonName}", cancellationToken: ct);
            
            // TODO: Update appointment in database
            // await _appointmentRepo.UpdateSurgeonAsync(appointmentId, surgeonName, ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleSetLocationAsync(ITelegramBotClient bot, long chatId, int messageId, string locationName, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, $"📍 Lugar actualizado a: {locationName}", cancellationToken: ct);
            
            // TODO: Update appointment in database
            // await _appointmentRepo.UpdateLocationAsync(appointmentId, locationName, ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleBackAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, confirmationKeyboard, ct);
        }

        public Task<InlineKeyboardMarkup> CreateModificationKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();

            // Primera fila: Fecha y Hora
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📅 Fecha", $"modify_date_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("⏰ Hora", $"modify_time_{appointment.Id}")
            });

            // Segunda fila: Lugar y Cirujano
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🏥 Lugar", $"modify_location_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("👨‍⚕️ Cirujano", $"modify_surgeon_{appointment.Id}")
            });

            // Tercera fila: Cirugía y Cantidad
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔬 Cirugía", $"modify_surgery_{appointment.Id}"),
                InlineKeyboardButton.WithCallbackData("🔢 Cantidad", $"modify_quantity_{appointment.Id}")
            });

            // Cuarta fila: Anestesiólogo
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("💉 Anestesiólogo", $"modify_anesthesiologist_{appointment.Id}")
            });

            // Quinta fila: Cancelar
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("❌ Cancelar", $"cancel_modification_{appointment.Id}")
            });

            return Task.FromResult(new InlineKeyboardMarkup(buttons));
        }
        
        private async Task HandleModifyFieldAsync(ITelegramBotClient bot, long chatId, int messageId, string callbackData, CancellationToken ct)
        {
            try
            {
                // Parse: modify_date_123, modify_time_123, etc.
                var parts = callbackData.Split('_');
                if (parts.Length < 3) return;
                
                var fieldType = parts[1]; // date, time, location, etc.
                var appointmentId = parts[2];
                
                var responseMessage = fieldType switch
                {
                    "date" => "📅 <b>¿Cuál es la nueva fecha?</b>\n\n💡 Ejemplos: \"25/09\", \"mañana\", \"el lunes\", \"23/12/2025\"",
                    "time" => "⏰ <b>¿Cuál es el nuevo horario?</b>\n\n💡 Ejemplos: \"16hs\", \"14:30\", \"8 de la mañana\"",
                    "location" => "🏥 <b>¿Cuál es el nuevo lugar?</b>\n\n💡 Ejemplos: \"Sanatorio Anchorena\", \"Hospital Italiano\", \"Clínica Santa Isabel\"",
                    "surgeon" => "👨‍⚕️ <b>¿Cuál es el nuevo cirujano?</b>\n\n💡 Ejemplos: \"Dr. García\", \"Rodriguez\", \"Dra. Martinez López\"",
                    "surgery" => "🔬 <b>¿Cuál es el nuevo tipo de cirugía?</b>\n\n💡 Ejemplos: \"CERS\", \"apendicectomía\", \"cesárea\", \"adenoides\"",
                    "quantity" => "🔢 <b>¿Cuál es la nueva cantidad?</b>\n\n💡 Ejemplos: \"2\", \"3 cirugías\", \"una sola\"",
                    "anesthesiologist" => "💉 <b>¿Cuál es el nuevo anestesiólogo?</b>\n\n💡 Ejemplos: \"Dr. Pérez\", \"sin anestesiólogo\", \"no asignar\"",
                    _ => "Escribí el nuevo valor y yo te ayudo a procesarlo."
                };
                
                responseMessage += "\n\n❌ Escribí <b>\"cancelar\"</b> si querés empezar de nuevo.";
                
                await MessageSender.SendWithRetry(chatId, responseMessage, cancellationToken: ct);
                
                // Remove the inline keyboard from the previous message
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling modify field callback for chat {ChatId}", chatId);
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando la modificación. Escribí **\"cancelar\"** para empezar de nuevo.", cancellationToken: ct);
            }
        }
        
        private async Task HandleCancelModificationAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            try
            {
                await MessageSender.SendWithRetry(chatId, "❌ Modificación cancelada. Podés empezar de nuevo enviando los datos de tu cirugía.", cancellationToken: ct);
                
                // Remove the inline keyboard
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling cancel modification for chat {ChatId}", chatId);
            }
        }
        
        private async Task HandleHelpOptionAsync(ITelegramBotClient bot, long chatId, int messageId, string option, CancellationToken ct)
        {
            try
            {
                var responseMessage = option switch
                {
                    "schedule" => 
                        "📅 <b>AGENDAR CIRUGÍA</b>\n\n" +
                        "Simplemente escribí los datos de tu cirugía en lenguaje natural:\n\n" +
                        "💡 <b>Ejemplos:</b>\n" +
                        "• \"Mañana 14hs CERS con Quiroga en Anchorena\"\n" +
                        "• \"23/09 2 adenoides + 1 MLD García Hospital Italiano\"\n" +
                        "• \"Lunes 16hs apendicectomía Dr. Rodriguez\"\n\n" +
                        "🎯 <b>Tips:</b>\n" +
                        "• Incluí fecha, hora, tipo de cirugía, cirujano y lugar\n" +
                        "• Podés usar mensajes de voz\n" +
                        "• Si falta algo, te lo voy a preguntar\n" +
                        "• Automáticamente se crea en tu Google Calendar",

                    "modify" => 
                        "✏️ <b>MODIFICAR CIRUGÍA</b>\n\n" +
                        "Para modificar una cirugía existente:\n\n" +
                        "💡 <b>Ejemplos:</b>\n" +
                        "• \"Quiero cambiar la cirugía de García del 23/09\"\n" +
                        "• \"Modificar la hora de la CERS del lunes a las 16hs\"\n" +
                        "• \"Cambiar el lugar de la cirugía de mañana al Italiano\"\n\n" +
                        "🎯 <b>Tips:</b>\n" +
                        "• Mencioná algún dato que identifique la cirugía (cirujano, fecha)\n" +
                        "• Te voy a mostrar las opciones encontradas\n" +
                        "• Podés cambiar fecha, hora, lugar, cirujano, etc.",

                    "delete" => 
                        "❌ <b>ELIMINAR CIRUGÍA</b>\n\n" +
                        "Para eliminar una cirugía agendada:\n\n" +
                        "💡 <b>Ejemplos:</b>\n" +
                        "• \"Cancelar la cirugía de García del 23/09\"\n" +
                        "• \"Eliminar la CERS del lunes\"\n" +
                        "• \"Borrar la cirugía de mañana\"\n\n" +
                        "🎯 <b>Tips:</b>\n" +
                        "• Mencioná datos que identifiquen la cirugía\n" +
                        "• Te voy a pedir confirmación antes de eliminar\n" +
                        "• También se elimina del Google Calendar",

                    "reports" => 
                        "📊 <b>REPORTES</b>\n\n" +
                        "Para ver resúmenes de tus cirugías:\n\n" +
                        "💡 <b>Comandos:</b>\n" +
                        "• <b>/semanal</b> - Cirugías de esta semana\n" +
                        "• <b>/mensual</b> - Cirugías del último mes\n\n" +
                        "🎯 <b>Tips:</b>\n" +
                        "• Los reportes muestran fecha, hora, lugar, cirujano\n" +
                        "• Se ordenan por fecha\n" +
                        "• Incluyen todas las cirugías confirmadas",

                    "more" => 
                        "❓ <b>MÁS AYUDA</b>\n\n" +
                        "🔧 <b>Comandos útiles:</b>\n" +
                        "• <b>/ayuda</b> - Mostrar este menú\n" +
                        "• <b>/semanal</b> - Reporte semanal\n" +
                        "• <b>/mensual</b> - Reporte mensual\n" +
                        "• <b>cancelar</b> - Cancelar operación actual\n\n" +
                        "💬 <b>Características:</b>\n" +
                        "• Acepto mensajes de voz 🎤\n" +
                        "• Proceso múltiples cirugías juntas\n" +
                        "• Sincronización con Google Calendar\n" +
                        "• Invitaciones automáticas a anestesiólogos\n\n" +
                        "🚀 <b>¡Empezá escribiendo tu cirugía!</b>",

                    _ => "❓ Opción no reconocida. Escribí <b>/ayuda</b> para ver el menú principal."
                };

                responseMessage += "\n\n🔙 Escribí <b>/ayuda</b> para volver al menú principal.";

                await MessageSender.SendWithRetry(chatId, responseMessage, cancellationToken: ct);
                
                // Remove the inline keyboard from the help message
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling help option {Option} for chat {ChatId}", option, chatId);
                await MessageSender.SendWithRetry(chatId, "❌ Error mostrando la ayuda. Escribí <b>/ayuda</b> para intentar de nuevo.", cancellationToken: ct);
            }
        }
    }
}