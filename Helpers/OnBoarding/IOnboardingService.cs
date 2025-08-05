using System;
using RegistroCx.BaseDeDatos;
using Telegram.Bot;

namespace RegistroCx.Helpers.OnBoarding;

public interface IOnboardingService
{
    /// <summary>
    /// Procesa un mensaje de usuario en función de su estado.
    /// Retorna (handled, profile).
    /// handled = true significa que YA se respondió algo al usuario y NO debe continuar el flujo principal.
    /// </summary>
    Task<(bool handled, UserProfile profile)> HandleAsync(
        ITelegramBotClient bot,
        long chatId,
        string rawText,
        string? phoneFromContact,
        CancellationToken ct);
}

