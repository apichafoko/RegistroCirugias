using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using RegistroCx.Domain;
using RegistroCx.Services.Repositories;
using RegistroCx.Services;  // SendMessage extension
using RegistroCx.Helpers._0Auth;
using RegistroCx.ProgramServices.Services.Telegram; // IGoogleOAuthService

namespace RegistroCx.Services.Onboarding
{
    public class OnboardingService : IOnboardingService
    {
        private readonly IUserProfileRepository _repo;
        private readonly IGoogleOAuthService _oauth;

        public OnboardingService(
            IUserProfileRepository repo,
            IGoogleOAuthService oauth)
        {
            _repo = repo;
            _oauth = oauth;
        }

        public async Task<(bool handled, UserProfile profile)> HandleAsync(
            ITelegramBotClient bot,
            long chatId,
            string rawText,
            string? phoneFromContact,
            CancellationToken ct)
        {
            rawText ??= "";
            var lower = rawText.Trim().ToLowerInvariant();
            var profile = await _repo.GetOrCreateAsync(chatId, ct);

            // /start
            if (lower == "/start")
            {
                if (profile.State == UserState.Ready)
                {
                    await EnviarBienvenida(bot, chatId, ct);
                    return (true, profile);
                }
                profile.State = UserState.NeedPhone;
                await _repo.SaveAsync(profile, ct);
                await EnviarBienvenida(bot, chatId, ct);
                return (true, profile);
            }

            // compartir teléfono
            if (!string.IsNullOrWhiteSpace(phoneFromContact))
            {
                profile.Phone = NormalizarTelefono(phoneFromContact);
                profile.State = UserState.NeedEmail;
                await _repo.SaveAsync(profile, ct);
                await MessageSender.SendWithRetry(chatId,
                            "Perfecto ✅. Ahora pasame tu email de Google (ej: nombre@gmail.com).",
                            cancellationToken: ct);
                return (true, profile);
            }

            if (profile.State == UserState.Ready)
                return (false, profile);

            switch (profile.State)
            {
                case UserState.NeedPhone:
                    if (!string.IsNullOrWhiteSpace(profile.Phone))
                    {
                        // Ya lo obtuvimos (probablemente por contacto)
                        profile.State = UserState.NeedEmail;
                        await _repo.SaveAsync(profile, ct);
                        await MessageSender.SendWithRetry(chatId,
                            "Perfecto ✅. Ahora pasame tu email de Google (ej: nombre@gmail.com).",
                            cancellationToken: ct);
                        return (true, profile);
                    }
                    if (TryExtractPhone(rawText, out var phoneManual))
                    {
                        profile.Phone = phoneManual;
                        profile.State = UserState.NeedEmail;
                        await _repo.SaveAsync(profile, ct);
                        await MessageSender.SendWithRetry(chatId,
                            "Perfecto ✅. Ahora pasame tu email de Google (ej: nombre@gmail.com).",
                            cancellationToken: ct);
                    }
                    else
                    {
                        await PedirTelefono(bot, chatId, ct);
                    }
                    return (true, profile);

                case UserState.NeedEmail:
                    if (Regex.IsMatch(rawText, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    {
                        profile.GoogleEmail = rawText.Trim();
                        profile.State = UserState.NeedOAuth;
                        await _repo.SaveAsync(profile, ct);
                        await MessageSender.SendWithRetry(chatId,
                            "Genial ✅. Escribí *continuar* para autorizar Calendar.",
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId,
                            "Email inválido. Ejemplo: algo@gmail.com",
                            cancellationToken: ct);
                    }
                    return (true, profile);

                case UserState.NeedOAuth:
                    if (lower == "continuar")
                    {
                        profile.State = UserState.PendingOAuth;
                        profile.OAuthNonce = Guid.NewGuid().ToString("N");
                        await _repo.SaveAsync(profile, ct);

                        // Generar URL real
                        var url = _oauth.BuildAuthUrl(chatId, profile.GoogleEmail!);
                        await MessageSender.SendWithRetry(chatId,
                            $"Abri este enlace para autorizar:\n{url}\nLuego escribí *ok*.",
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId,
                            "Escribí *continuar* para generar el enlace.",
                            cancellationToken: ct);
                    }
                    return (true, profile);

                case UserState.PendingOAuth:
                    if (lower == "ok")
                    {
                        // en callback ya se guardaron tokens
                        profile.State = UserState.Ready;
                        await _repo.SaveAsync(profile, ct);
                        await MessageSender.SendWithRetry(chatId,
                            "✅ Autorización completa. Ya podés enviar cirugías.",
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId,
                            "Cuando autorices, escribí *ok*.",
                            cancellationToken: ct);
                    }
                    return (true, profile);
            }

            return (false, profile);
        }

        private async Task EnviarBienvenida(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var profile = await _repo.GetOrCreateAsync(chatId, ct);

            if (profile.State == UserState.Ready)
            {
            var txt =
    @"Hola ¿cómo estás? Soy tu asistente para registrar cirugías.
    Podemos hacer 3 cosas:
    1) Escribí ""/semanal"" para que te envíe el resumen de esta semana.
    2) Escribí ""/mensual"" para que te envíe todo lo que pasó en el último mes.
    3) Mandame los datos de cualquier cirugía para que los pueda registrar en tu calendario.";
            await MessageSender.SendWithRetry(chatId, txt, cancellationToken: ct);
            }
            else
            {
            var kb = new ReplyKeyboardMarkup(new[]
            {
                KeyboardButton.WithRequestContact("📱 Compartir mi teléfono")
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            var txt =
    @"¡Hola! 👋 Soy el asistente de cirugías.
    1) Compartí tu teléfono.
    2) Luego tu email de Google.
    3) Autoriza Calendar
    4) Envía la cirugía";

            await MessageSender.SendWithRetry(chatId, txt, replyMarkup: kb, cancellationToken: ct);
            }
        }

        private Task PedirTelefono(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var kb = new ReplyKeyboardMarkup(new[]
            {
                KeyboardButton.WithRequestContact("📱 Compartir mi teléfono")
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
            return bot.SendMessage(
                chatId,
                "Necesito tu teléfono (ej: +54911…).",
                replyMarkup: kb,
                cancellationToken: ct);
        }

        private static string NormalizarTelefono(string s)
        {
            var digits = new string(s.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return digits.StartsWith("+") ? digits : "+" + digits;
        }

        private static bool TryExtractPhone(string input, out string phone)
        {
            phone = "";
            if (string.IsNullOrWhiteSpace(input)) return false;
            var filtered = new string(input.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
            if (!filtered.StartsWith("+") && filtered.StartsWith("54"))
                filtered = "+" + filtered;
            if (filtered.Length < 8) return false;
            phone = filtered;
            return true;
        }


        private static bool IsValidEmail(string s) =>
            !string.IsNullOrWhiteSpace(s) &&
            System.Text.RegularExpressions.Regex.IsMatch(
                s.Trim(),
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
