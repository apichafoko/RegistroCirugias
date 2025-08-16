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
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services.UI
{
    public class QuickEditService : IQuickEditService
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<QuickEditService> _logger;
        
        // Callback data prefixes
        private const string CONFIRM_PREFIX = "confirm_";
        private const string EDIT_PREFIX = "edit_";
        private const string CANCEL_PREFIX = "cancel_";
        private const string DATE_PREFIX = "date_";
        private const string TIME_PREFIX = "time_";
        private const string SURGEON_PREFIX = "surgeon_";
        private const string LOCATION_PREFIX = "location_";
        private const string BACK_PREFIX = "back_";

        public QuickEditService(ICacheService cacheService, ILogger<QuickEditService> logger)
        {
            _cacheService = cacheService;
            _logger = logger;
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
                        await HandleCancelAsync(bot, chatId, messageId, parameter, ct);
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
            await MessageSender.SendWithRetry(chatId, "✅ Cirugía confirmada exitosamente.", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
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
    }
}