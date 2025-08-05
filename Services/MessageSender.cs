using System;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace RegistroCx.Services;

// En una nueva clase MessageSender.cs
public static class MessageSender
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;

    public static async Task SendWithRetry(ITelegramBotClient bot, long chatId, string message, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await bot.SendMessage(chatId, message, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var delay = ex.Parameters?.RetryAfter ?? (BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxRetries - 1)
                {
                    Console.WriteLine($"Failed to send message after {MaxRetries} attempts: {ex.Message}");
                    return;
                }
                var delay = TimeSpan.FromMilliseconds(BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
        }
    }
}
