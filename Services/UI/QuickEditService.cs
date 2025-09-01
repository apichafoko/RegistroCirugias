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
using RegistroCx.Services.Repositories;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services.UI
{
    public class QuickEditService : IQuickEditService
    {
        private readonly ICacheService _cacheService;
        private readonly ILogger<QuickEditService> _logger;
        private readonly AppointmentConfirmationService? _confirmationService;
        private readonly Dictionary<long, Appointment>? _pendingAppointments;
        private readonly IAnesthesiologistRepository _anesthesiologistRepo;
        private readonly ISurgeonRepository _surgeonRepo;
        private readonly EquipoService _equipoService;
        
        // Dictionary to track pending edit states for "other" options
        private static readonly Dictionary<long, (string action, long appointmentId)> _pendingEdits = new();
        
        // Callback data prefixes
        private const string CONFIRM_PREFIX = "confirm_";
        private const string EDIT_PREFIX = "edit_";
        private const string CANCEL_PREFIX = "cancel_";
        private const string DATE_PREFIX = "date_";
        private const string TIME_PREFIX = "time_";
        private const string SURGEON_PREFIX = "surgeon_";
        private const string LOCATION_PREFIX = "location_";
        private const string BACK_PREFIX = "back_";

        public QuickEditService(
            ICacheService cacheService, 
            ILogger<QuickEditService> logger, 
            IAnesthesiologistRepository anesthesiologistRepo,
            ISurgeonRepository surgeonRepo,
            EquipoService equipoService,
            AppointmentConfirmationService? confirmationService = null, 
            Dictionary<long, Appointment>? pendingAppointments = null)
        {
            _cacheService = cacheService;
            _logger = logger;
            _confirmationService = confirmationService;
            _pendingAppointments = pendingAppointments;
            _anesthesiologistRepo = anesthesiologistRepo;
            _surgeonRepo = surgeonRepo;
            _equipoService = equipoService;
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

        public async Task<InlineKeyboardMarkup> GenerateSurgeonSelectionKeyboardAsync(long chatId)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Get team-specific surgeons from database
            var equipoId = await _equipoService.ObtenerPrimerEquipoIdPorChatIdAsync(chatId);
            var teamSurgeons = await _surgeonRepo.GetNamesByEquipoAsync(equipoId, CancellationToken.None);

            // Add surgeons in rows of 2
            for (int i = 0; i < teamSurgeons.Count; i += 2)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 2 && i + j < teamSurgeons.Count; j++)
                {
                    var surgeon = teamSurgeons[i + j];
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

        public async Task<InlineKeyboardMarkup> GenerateAnesthesiologistSelectionKeyboardAsync(long chatId)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            try
            {
                // Get team-specific anesthesiologists from database
                var equipoId = await _equipoService.ObtenerPrimerEquipoIdPorChatIdAsync(chatId, CancellationToken.None);
                var teamAnesthesiologists = await _anesthesiologistRepo.GetNamesByEquipoAsync(equipoId, CancellationToken.None);

                // Add team anesthesiologists in rows of 1 (names can be long)
                foreach (var anesthesiologist in teamAnesthesiologists)
                {
                    buttons.Add(new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(anesthesiologist, $"setanesthesiologist_{anesthesiologist}")
                    });
                }
            }
            catch
            {
                // Fallback to cache if team lookup fails
                var commonAnesthesiologists = await _cacheService.GetAnesthesiologistNamesAsync();
                foreach (var anesthesiologist in commonAnesthesiologists)
                {
                    buttons.Add(new List<InlineKeyboardButton>
                    {
                        InlineKeyboardButton.WithCallbackData(anesthesiologist, $"setanesthesiologist_{anesthesiologist}")
                    });
                }
            }

            // Add "Other anesthesiologist" and "No anesthesiologist" options
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("💉 Otro anestesiólogo", "otheranesthesiologist"),
                InlineKeyboardButton.WithCallbackData("🚫 Sin anestesiólogo", "noanesthesiologist")
            });

            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", "back_edit")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        public Task<InlineKeyboardMarkup> GenerateSurgerySelectionKeyboardAsync()
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Common surgery types
            var commonSurgeries = new[]
            {
                "CERS", "Adenoides", "Amígdalas", "Septoplastía", "Rinoplastía",
                "Miringoplastía", "Timpanoplastía", "Polipectomía", "Sinusitis",
                "Mastoidectomía", "Laringoscopía", "Traqueostomía"
            };

            // Add surgeries in rows of 2
            for (int i = 0; i < commonSurgeries.Length; i += 2)
            {
                var row = new List<InlineKeyboardButton>();
                for (int j = 0; j < 2 && i + j < commonSurgeries.Length; j++)
                {
                    var surgery = commonSurgeries[i + j];
                    row.Add(InlineKeyboardButton.WithCallbackData(surgery, $"setsurgery_{surgery}"));
                }
                buttons.Add(row);
            }

            // Add "Other surgery" option
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔬 Otra cirugía", "othersurgery"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", "back_edit")
            });

            return Task.FromResult(new InlineKeyboardMarkup(buttons));
        }

        public InlineKeyboardMarkup GenerateQuantitySelectionKeyboard(Appointment appointment)
        {
            var buttons = new List<List<InlineKeyboardButton>>();
            
            // Common quantities (1-5)
            var row1 = new List<InlineKeyboardButton>();
            var row2 = new List<InlineKeyboardButton>();
            
            for (int i = 1; i <= 5; i++)
            {
                var quantityText = i == 1 ? "1 cirugía" : $"{i} cirugías";
                var button = InlineKeyboardButton.WithCallbackData(quantityText, $"setquantity_{appointment.Id}_{i}");
                
                if (i <= 3)
                    row1.Add(button);
                else
                    row2.Add(button);
            }
            
            buttons.Add(row1);
            buttons.Add(row2);

            // Add "Other quantity" option
            buttons.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("🔢 Otra cantidad", "otherquantity"),
                InlineKeyboardButton.WithCallbackData("⬅️ Volver", $"edit_{appointment.Id}")
            });

            return new InlineKeyboardMarkup(buttons);
        }

        private string FormatAppointmentForConfirmation(Appointment appointment)
        {
            var dateStr = appointment.FechaHora?.ToString("dd/MM/yyyy HH:mm") ?? "Fecha pendiente";
            var quantityStr = appointment.Cantidad?.ToString() ?? "Cantidad pendiente";
            var anesthesiologistStr = string.IsNullOrEmpty(appointment.Anestesiologo) ? "Sin anestesiólogo" : appointment.Anestesiologo;
            
            return $"📅 <b>{dateStr}</b>\n" +
                   $"🏥 <b>{appointment.Lugar ?? "Lugar pendiente"}</b>\n" +
                   $"👨‍⚕️ <b>{appointment.Cirujano ?? "Cirujano pendiente"}</b>\n" +
                   $"🔬 <b>{appointment.Cirugia ?? "Cirugía pendiente"}</b>\n" +
                   $"🔢 <b>{quantityStr}</b>\n" +
                   $"💉 <b>{anesthesiologistStr}</b>";
        }

        public async Task<bool> HandleCallbackQueryAsync(ITelegramBotClient bot, long chatId, string callbackData, int messageId, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Handling callback query: {CallbackData}", callbackData);

                // Handle callbacks without underscore first
                switch (callbackData)
                {
                    case "othersurgeon":
                        await HandleOtherSurgeonAsync(bot, chatId, messageId, ct);
                        return true;
                    case "otheranesthesiologist":
                        await HandleOtherAnesthesiologistAsync(bot, chatId, messageId, ct);
                        return true;
                    case "othersurgery":
                        await HandleOtherSurgeryAsync(bot, chatId, messageId, ct);
                        return true;
                    case "otherlocation":
                        await HandleOtherLocationAsync(bot, chatId, messageId, ct);
                        return true;
                    case "otherquantity":
                        await HandleOtherQuantityAsync(bot, chatId, messageId, ct);
                        return true;
                    case "otherdate":
                        await HandleOtherDateAsync(bot, chatId, messageId, ct);
                        return true;
                    case "othertime":
                        await HandleOtherTimeAsync(bot, chatId, messageId, ct);
                        return true;
                    case "noanesthesiologist":
                        await HandleNoAnesthesiologistAsync(bot, chatId, messageId, ct);
                        return true;
                    case "back_edit":
                        await HandleBackEditAsync(bot, chatId, messageId, ct);
                        return true;
                }

                // Parse callback data with underscore
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

                    case "anesthesiologist":
                        await HandleAnesthesiologistEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setanesthesiologist":
                        await HandleSetAnesthesiologistAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "noanesthesiologist":
                        await HandleNoAnesthesiologistAsync(bot, chatId, messageId, ct);
                        return true;

                    case "surgery":
                        await HandleSurgeryEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setsurgery":
                        await HandleSetSurgeryAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "quantity":
                        await HandleQuantityEditAsync(bot, chatId, messageId, parameter, ct);
                        return true;

                    case "setquantity":
                        await HandleSetQuantityAsync(bot, chatId, messageId, parameter, ct);
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
            var surgeonKeyboard = await GenerateSurgeonSelectionKeyboardAsync(chatId);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, surgeonKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "👨‍⚕️ Selecciona el cirujano:", cancellationToken: ct);
        }

        private async Task HandleLocationEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var locationKeyboard = await GenerateLocationSelectionKeyboardAsync();
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, locationKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "📍 Selecciona el lugar:", cancellationToken: ct);
        }

        private async Task HandleAnesthesiologistEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var anesthesiologistKeyboard = await GenerateAnesthesiologistSelectionKeyboardAsync(chatId);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, anesthesiologistKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "💉 Selecciona el anestesiólogo:", cancellationToken: ct);
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
                    // Update appointment in pending appointments if available
                    if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
                    {
                        // Mantener la hora existente, solo cambiar fecha
                        if (appointment.FechaHora.HasValue)
                        {
                            var time = appointment.FechaHora.Value.TimeOfDay;
                            appointment.FechaHora = newDate.Date.Add(time);
                        }
                        else
                        {
                            appointment.FechaHora = newDate;
                        }
                        
                        // Show updated appointment with confirmation buttons
                        var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                        var appointmentText = FormatAppointmentForConfirmation(appointment);
                        
                        await MessageSender.SendWithRetry(chatId, 
                            $"📅 Fecha actualizada a: {newDate:dd/MM/yyyy}\n\n{appointmentText}", 
                            replyMarkup: confirmationKeyboard,
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId, $"📅 Fecha actualizada a: {newDate:dd/MM/yyyy}", cancellationToken: ct);
                    }
                    
                    // Remove inline keyboard from previous message
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
                
                // Update appointment in pending appointments if available
                if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
                {
                    if (TimeOnly.TryParse(timeStr, out var newTime))
                    {
                        // Mantener la fecha existente, solo cambiar hora
                        if (appointment.FechaHora.HasValue)
                        {
                            var date = appointment.FechaHora.Value.Date;
                            appointment.FechaHora = date.Add(newTime.ToTimeSpan());
                        }
                        else
                        {
                            appointment.FechaHora = DateTime.Today.Add(newTime.ToTimeSpan());
                        }
                        
                        // Show updated appointment with confirmation buttons
                        var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                        var appointmentText = FormatAppointmentForConfirmation(appointment);
                        
                        await MessageSender.SendWithRetry(chatId, 
                            $"🕒 Hora actualizada a: {timeStr}\n\n{appointmentText}", 
                            replyMarkup: confirmationKeyboard,
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId, $"❌ Error: hora inválida {timeStr}", cancellationToken: ct);
                    }
                }
                else
                {
                    await MessageSender.SendWithRetry(chatId, $"🕒 Hora actualizada a: {timeStr}", cancellationToken: ct);
                }
                
                // Remove inline keyboard from previous message
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
        }

        private async Task HandleSetSurgeonAsync(ITelegramBotClient bot, long chatId, int messageId, string surgeonName, CancellationToken ct)
        {
            // Update appointment in pending appointments if available
            if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
            {
                appointment.Cirujano = surgeonName;
                
                // Show updated appointment with confirmation buttons
                var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                var appointmentText = FormatAppointmentForConfirmation(appointment);
                
                await MessageSender.SendWithRetry(chatId, 
                    $"👨‍⚕️ Cirujano actualizado a: {surgeonName}\n\n{appointmentText}", 
                    replyMarkup: confirmationKeyboard,
                    cancellationToken: ct);
            }
            else
            {
                await MessageSender.SendWithRetry(chatId, $"👨‍⚕️ Cirujano actualizado a: {surgeonName}", cancellationToken: ct);
            }
            
            // Remove inline keyboard from previous message
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleSetLocationAsync(ITelegramBotClient bot, long chatId, int messageId, string locationName, CancellationToken ct)
        {
            // Update appointment in pending appointments if available
            if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
            {
                appointment.Lugar = locationName;
                
                // Show updated appointment with confirmation buttons
                var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                var appointmentText = FormatAppointmentForConfirmation(appointment);
                
                await MessageSender.SendWithRetry(chatId, 
                    $"📍 Lugar actualizado a: {locationName}\n\n{appointmentText}", 
                    replyMarkup: confirmationKeyboard,
                    cancellationToken: ct);
            }
            else
            {
                await MessageSender.SendWithRetry(chatId, $"📍 Lugar actualizado a: {locationName}", cancellationToken: ct);
            }
            
            // Remove inline keyboard from previous message
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleSetAnesthesiologistAsync(ITelegramBotClient bot, long chatId, int messageId, string anesthesiologistName, CancellationToken ct)
        {
            // Update appointment in pending appointments if available
            if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
            {
                appointment.Anestesiologo = anesthesiologistName;
                
                // Show updated appointment with confirmation buttons
                var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                var appointmentText = FormatAppointmentForConfirmation(appointment);
                
                await MessageSender.SendWithRetry(chatId, 
                    $"💉 Anestesiólogo actualizado a: {anesthesiologistName}\n\n{appointmentText}", 
                    replyMarkup: confirmationKeyboard,
                    cancellationToken: ct);
            }
            else
            {
                await MessageSender.SendWithRetry(chatId, $"💉 Anestesiólogo actualizado a: {anesthesiologistName}", cancellationToken: ct);
            }
            
            // Remove inline keyboard from previous message
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleNoAnesthesiologistAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, "🚫 Anestesiólogo removido (sin anestesiólogo)", cancellationToken: ct);
            
            // TODO: Update appointment in database to remove anesthesiologist
            // await _appointmentRepo.UpdateAnesthesiologistAsync(appointmentId, null, ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherAnesthesiologistAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            // Set pending edit state
            _pendingEdits[chatId] = ("anesthesiologist", 0); // appointmentId will be extracted from context
            
            await MessageSender.SendWithRetry(chatId, "💉 Escribí el nombre del anestesiólogo:", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleSurgeryEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var surgeryKeyboard = await GenerateSurgerySelectionKeyboardAsync();
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, surgeryKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "🔬 Selecciona el tipo de cirugía:", cancellationToken: ct);
        }

        private async Task HandleSetSurgeryAsync(ITelegramBotClient bot, long chatId, int messageId, string surgeryType, CancellationToken ct)
        {
            // Update appointment in pending appointments if available
            if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
            {
                appointment.Cirugia = surgeryType;
                
                // Show updated appointment with confirmation buttons
                var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                var appointmentText = FormatAppointmentForConfirmation(appointment);
                
                await MessageSender.SendWithRetry(chatId, 
                    $"🔬 Cirugía actualizada a: {surgeryType}\n\n{appointmentText}", 
                    replyMarkup: confirmationKeyboard,
                    cancellationToken: ct);
            }
            else
            {
                await MessageSender.SendWithRetry(chatId, $"🔬 Cirugía actualizada a: {surgeryType}", cancellationToken: ct);
            }
            
            // Remove inline keyboard from previous message
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherSurgeryAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            // Set pending edit state
            _pendingEdits[chatId] = ("surgery", 0); // appointmentId will be extracted from context
            
            await MessageSender.SendWithRetry(chatId, "🔬 Escribí el tipo de cirugía:", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleQuantityEditAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var quantityKeyboard = GenerateQuantitySelectionKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, quantityKeyboard, ct);
            await MessageSender.SendWithRetry(chatId, "🔢 Selecciona la cantidad:", cancellationToken: ct);
        }

        private async Task HandleSetQuantityAsync(ITelegramBotClient bot, long chatId, int messageId, string parameter, CancellationToken ct)
        {
            var parts = parameter.Split('_');
            if (parts.Length >= 2)
            {
                var appointmentId = parts[0];
                var quantity = parts[1];
                
                // Update appointment in pending appointments if available
                if (_pendingAppointments != null && _pendingAppointments.TryGetValue(chatId, out var appointment))
                {
                    appointment.Cantidad = int.Parse(quantity);
                    
                    // Show updated appointment with confirmation buttons
                    var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
                    var appointmentText = FormatAppointmentForConfirmation(appointment);
                    
                    await MessageSender.SendWithRetry(chatId, 
                        $"🔢 Cantidad actualizada a: {quantity}\n\n{appointmentText}", 
                        replyMarkup: confirmationKeyboard,
                        cancellationToken: ct);
                }
                else
                {
                    await MessageSender.SendWithRetry(chatId, $"🔢 Cantidad actualizada a: {quantity}", cancellationToken: ct);
                }
                
                // Remove inline keyboard from previous message
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
            }
        }

        private async Task HandleOtherQuantityAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, "🔢 Escribí la cantidad de cirugías:", cancellationToken: ct);
            
            // TODO: Set state to capture quantity input
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherSurgeonAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            // Set pending edit state
            _pendingEdits[chatId] = ("surgeon", 0); // appointmentId will be extracted from context
            
            await MessageSender.SendWithRetry(chatId, "👨‍⚕️ Escribí el nombre del cirujano:", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherLocationAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            // Set pending edit state
            _pendingEdits[chatId] = ("location", 0); // appointmentId will be extracted from context
            
            await MessageSender.SendWithRetry(chatId, "📍 Escribí el nombre del lugar:", cancellationToken: ct);
            
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherDateAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, "📅 Escribí la nueva fecha (ej: 25/12, mañana, el lunes):", cancellationToken: ct);
            
            // TODO: Set state to capture date input
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleOtherTimeAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            await MessageSender.SendWithRetry(chatId, "🕒 Escribí la nueva hora (ej: 14:30, 8hs, 16 de la tarde):", cancellationToken: ct);
            
            // TODO: Set state to capture time input
            // Remove inline keyboard
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
        }

        private async Task HandleBackAsync(ITelegramBotClient bot, long chatId, int messageId, string appointmentId, CancellationToken ct)
        {
            var appointment = new Appointment { Id = long.Parse(appointmentId) };
            var confirmationKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await UpdateMessageKeyboardAsync(bot, chatId, messageId, confirmationKeyboard, ct);
        }

        private async Task HandleBackEditAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
        {
            // For back_edit, try to get the appointment from pending appointments
            if (_pendingAppointments?.Values.FirstOrDefault() is { } appointment)
            {
                var editKeyboard = GenerateEditKeyboard(appointment);
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, editKeyboard, ct);
            }
            else
            {
                // No appointment context, just remove keyboard
                await UpdateMessageKeyboardAsync(bot, chatId, messageId, new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton[]>()), ct);
                await MessageSender.SendWithRetry(chatId, "⬅️ Volviendo al menú principal.", cancellationToken: ct);
            }
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
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando la modificación. Escribí <b>\"cancelar\"</b> para empezar de nuevo.", cancellationToken: ct);
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

        // Method to handle text input when user has pending edit state
        public async Task<bool> TryHandleTextInputAsync(ITelegramBotClient bot, long chatId, string text, CancellationToken ct)
        {
            if (!_pendingEdits.TryGetValue(chatId, out var pendingEdit))
            {
                return false; // No pending edit state
            }

            // Remove the pending state
            _pendingEdits.Remove(chatId);

            try
            {
                // Get the most recent appointment for this user to edit
                var appointment = await GetMostRecentAppointmentForUserAsync(chatId, ct);
                if (appointment == null)
                {
                    await MessageSender.SendWithRetry(chatId, "❌ No se encontró una cita para modificar.", cancellationToken: ct);
                    return true;
                }

                // Apply the edit based on the action
                switch (pendingEdit.action)
                {
                    case "anesthesiologist":
                        await ApplyAnesthesiologistEditAsync(bot, chatId, appointment, text, ct);
                        break;
                    case "surgeon":
                        await ApplySurgeonEditAsync(bot, chatId, appointment, text, ct);
                        break;
                    case "surgery":
                        await ApplySurgeryEditAsync(bot, chatId, appointment, text, ct);
                        break;
                    case "location":
                        await ApplyLocationEditAsync(bot, chatId, appointment, text, ct);
                        break;
                    default:
                        await MessageSender.SendWithRetry(chatId, "❌ Opción no válida.", cancellationToken: ct);
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing text input for chat {ChatId}, action {Action}", chatId, pendingEdit.action);
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando la entrada. Por favor, intenta de nuevo.", cancellationToken: ct);
                return true;
            }
        }

        private Task<Appointment?> GetMostRecentAppointmentForUserAsync(long chatId, CancellationToken ct)
        {
            // Check in pending appointments first
            if (_pendingAppointments?.Values.FirstOrDefault(a => true) is { } pendingAppt)
            {
                return Task.FromResult<Appointment?>(pendingAppt);
            }

            // TODO: Get from appointment repository if needed
            // For now, this is a placeholder - the appointment context should be tracked better
            return Task.FromResult<Appointment?>(null);
        }

        private async Task ApplyAnesthesiologistEditAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string anesthesiologistName, CancellationToken ct)
        {
            appointment.Anestesiologo = anesthesiologistName.Trim();
            
            var appointmentText = FormatAppointmentForConfirmation(appointment);
            var confirmKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await MessageSender.SendWithRetry(chatId,
                $"💉 Anestesiólogo actualizado a: {anesthesiologistName}\n\n{appointmentText}", 
                replyMarkup: confirmKeyboard, 
                cancellationToken: ct);
        }

        private async Task ApplySurgeonEditAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string surgeonName, CancellationToken ct)
        {
            appointment.Cirujano = surgeonName.Trim();
            
            var appointmentText = FormatAppointmentForConfirmation(appointment);
            var confirmKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await MessageSender.SendWithRetry(chatId,
                $"👨‍⚕️ Cirujano actualizado a: {surgeonName}\n\n{appointmentText}", 
                replyMarkup: confirmKeyboard, 
                cancellationToken: ct);
        }

        private async Task ApplySurgeryEditAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string surgeryType, CancellationToken ct)
        {
            appointment.Cirugia = surgeryType.Trim();
            
            var appointmentText = FormatAppointmentForConfirmation(appointment);
            var confirmKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await MessageSender.SendWithRetry(chatId,
                $"🔬 Cirugía actualizada a: {surgeryType}\n\n{appointmentText}", 
                replyMarkup: confirmKeyboard, 
                cancellationToken: ct);
        }

        private async Task ApplyLocationEditAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string location, CancellationToken ct)
        {
            appointment.Lugar = location.Trim();
            
            var appointmentText = FormatAppointmentForConfirmation(appointment);
            var confirmKeyboard = GenerateConfirmationKeyboard(appointment);
            
            await MessageSender.SendWithRetry(chatId,
                $"📍 Lugar actualizado a: {location}\n\n{appointmentText}", 
                replyMarkup: confirmKeyboard, 
                cancellationToken: ct);
        }
    }
}