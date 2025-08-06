using System;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.ReplyMarkups;

namespace RegistroCx.ProgramServices.Services.Telegram;

// En una nueva clase MessageSender.cs
public static class MessageSender
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;
    
    public static ITelegramBotClient? Bot { get; set; }

    public static async Task SendWithRetry(long chatId, string message, ReplyMarkup? replyMarkup = null, CancellationToken cancellationToken = default)
    {
        if (Bot == null)
            throw new InvalidOperationException("Bot instance not set. Set MessageSender.Bot before calling SendWithRetry.");
            
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await Bot.SendMessage(chatId, message, replyMarkup: replyMarkup, cancellationToken: cancellationToken);
                
                return;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429)
            {
                var delay = ex.Parameters?.RetryAfter ?? (BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxRetries - 1)
                {
                    Console.WriteLine($"Failed to send message after {MaxRetries} attempts: {ex.Message}");
                    return;
                }
                var delay = TimeSpan.FromMilliseconds(BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}