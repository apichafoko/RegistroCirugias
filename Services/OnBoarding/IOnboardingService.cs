using RegistroCx.Domain;
using Telegram.Bot;

namespace RegistroCx.Services.Onboarding
{
    public interface IOnboardingService
    {
        Task<(bool handled, UserProfile profile)> HandleAsync(
            ITelegramBotClient bot,
            long chatId,
            string rawText,
            string? phoneFromContact,
            long? telegramUserId,
            string? firstName,
            string? lastName,
            string? username,
            string? languageCode,
            CancellationToken ct);
    }
}
