using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using RegistroCx.Models;

namespace RegistroCx.Services.UI
{
    public interface IQuickEditService
    {
        // Generate keyboard for appointment confirmation
        InlineKeyboardMarkup GenerateConfirmationKeyboard(Appointment appointment);
        
        // Generate keyboard for appointment editing
        InlineKeyboardMarkup GenerateEditKeyboard(Appointment appointment);
        
        // Generate keyboard for field-specific editing
        InlineKeyboardMarkup GenerateDateSelectionKeyboard(Appointment appointment);
        InlineKeyboardMarkup GenerateTimeSelectionKeyboard(Appointment appointment);
        Task<InlineKeyboardMarkup> GenerateSurgeonSelectionKeyboardAsync();
        Task<InlineKeyboardMarkup> GenerateLocationSelectionKeyboardAsync();
        
        // Generate keyboard for modification selection
        Task<InlineKeyboardMarkup> CreateModificationKeyboard(Appointment appointment);
        
        // Handle callback queries from inline keyboards
        Task<bool> HandleCallbackQueryAsync(ITelegramBotClient bot, long chatId, string callbackData, int messageId, CancellationToken ct);
        
        // Send appointment with quick edit buttons
        Task SendAppointmentWithEditButtonsAsync(ITelegramBotClient bot, long chatId, Appointment appointment, string messageText, CancellationToken ct);
        
        // Update existing message with new keyboard
        Task UpdateMessageKeyboardAsync(ITelegramBotClient bot, long chatId, int messageId, InlineKeyboardMarkup newKeyboard, CancellationToken ct);
    }
}