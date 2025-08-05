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
            CancellationToken ct);
    }
}
