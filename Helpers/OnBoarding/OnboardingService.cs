/*
using System;
using RegistroCx.Services.Repositories;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace RegistroCx.Helpers.OnBoarding;

public class OnboardingService : IOnboardingService
{
    private readonly IUserProfileRepository _repo;
    private const bool ResetOnStart = true; // Poné true si querés que /start reinicie al usuario

    public OnboardingService(IUserProfileRepository repo)
    {
        _repo = repo;
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

        Console.WriteLine($"[ONBOARD-IN] chat={chatId} state={profile.State} phoneDb='{profile.Phone ?? "NULL"}' phoneFromContact='{phoneFromContact ?? "NULL"}' rawText='{rawText}'");

        // /start y ayuda primero (igual que antes)
        if (EsComandoStart(rawText))
        {
            if (profile.State == UserState.Ready)
            {
                MessageSender.SendWithRetry(chatId,
                    "Ya estás configurado ✅. Mandame una cirugía o escribí 'ayuda' para ejemplos.",
                    cancellationToken: ct);
            }
            else
            {
                if (profile.State == UserState.Unknown)
                {
                    profile.State = UserState.NeedPhone;
                    await _repo.SaveAsync(profile, ct);
                }
                await EnviarMensajeBienvenidaAsync(bot, chatId, profile, ct);
            }
            return (true, profile);
        }
        if (EsComandoAyuda(rawText.Trim().ToLowerInvariant()))
        {
            MessageSender.SendWithRetry(chatId, MensajeAyuda(), cancellationToken: ct);
            return (true, profile);
        }

        // ---- CONTACTO COMPARTIDO ----
        if (phoneFromContact != null)
        {
            // Normalizar y asignar SIEMPRE (incluso si ya había uno distinto; puedes cambiar esto si querés)
            var normalizado = NormalizarTelefono(phoneFromContact);
            if (string.IsNullOrWhiteSpace(profile.Phone) || profile.Phone != normalizado)
            {
                Console.WriteLine($"[ONBOARD-SET] Asignando phone='{phoneFromContact}' normalizado='{normalizado}'");
                profile.Phone = normalizado;
                if (string.IsNullOrWhiteSpace(profile.GoogleEmail))
                    profile.State = UserState.NeedEmail;
                else if (profile.State == UserState.NeedPhone)
                    profile.State = UserState.NeedEmail;

                await _repo.SaveAsync(profile, ct);

                var check = await _repo.GetAsync(chatId, ct);
                Console.WriteLine($"[ONBOARD-AFTER-SAVE] DB phone='{check?.Phone}' state={check?.State}");
            }
        }

        Console.WriteLine($"[ONBOARD-SWITCH] state={profile.State} phone='{profile.Phone}'");

        // 2. Si ya está listo -> no manejamos (flujo principal continúa)
        if (profile.State == UserState.Ready)
            return (false, profile);

        // 3. Máquina de estados
        switch (profile.State)
        {
            case UserState.Unknown:
                profile.State = UserState.NeedPhone;
                await _repo.SaveAsync(profile, ct);
                await PedirTelefonoAsync(bot, chatId, ct, mensajeInicial: true);
                return (true, profile);

            case UserState.NeedPhone:
                if (!string.IsNullOrWhiteSpace(profile.Phone))
                {
                    // Ya lo obtuvimos (probablemente por contacto)
                    profile.State = UserState.NeedEmail;
                    await _repo.SaveAsync(profile, ct);
                    MessageSender.SendWithRetry(chatId,
                        "Perfecto ✅. Ahora pasame tu email de Google (ej: nombre@gmail.com).",
                        cancellationToken: ct);
                    return (true, profile);
                }
                if (TryExtractPhone(rawText, out var phoneManual))
                {
                    profile.Phone = phoneManual;
                    profile.State = UserState.NeedEmail;
                    await _repo.SaveAsync(profile, ct);
                    MessageSender.SendWithRetry(chatId,
                        "Perfecto ✅. Ahora pasame tu email de Google (ej: nombre@gmail.com).",
                        cancellationToken: ct);
                }
                else
                {
                    await PedirTelefonoAsync(bot, chatId, ct);
                }
                return (true, profile);

            case UserState.NeedEmail:
                if (IsValidEmail(rawText))
                {
                    profile.GoogleEmail = rawText.Trim();
                    profile.State = UserState.NeedOAuth;
                    await _repo.SaveAsync(profile, ct);
                    MessageSender.SendWithRetry(chatId,
                        "Genial ✅. Escribí *continuar* cuando quieras autorizar acceso a tu Google Calendar.",
                        cancellationToken: ct);
                }
                else
                {
                    MessageSender.SendWithRetry(chatId,
                        "Pasame un email de Google válido (ej: algo@gmail.com).",
                        cancellationToken: ct);
                }
                return (true, profile);

            case UserState.NeedOAuth:
                if (lower == "continuar")
                {
                    if (string.IsNullOrWhiteSpace(profile.GoogleEmail))
                    {
                        MessageSender.SendWithRetry(chatId, "Antes necesito tu email de Google.", cancellationToken: ct);
                        return (true, profile);
                    }

                    /*
                    var url = _googleOAuth.BuildAuthUrl(chatId, profile.GoogleEmail);
                    MessageSender.SendWithRetry(chatId,
                        "Abrí este enlace para autorizar acceso a tu Calendar:\n" + url +
                        "\nCuando finalices la autorización, volvé y escribí 'ok'.",
                        cancellationToken: ct);
                    return (true, profile);
                }
                MessageSender.SendWithRetry(chatId,
                    "Escribí 'continuar' para generar el enlace de autorización.",
                    cancellationToken: ct);
                return (true, profile);

            case UserState.PendingOAuth:
                if (lower == "ok")
                {
                    // Verificar si ya tenemos tokens
                    var refreshed = !string.IsNullOrWhiteSpace(profile.GoogleAccessToken);
                    if (refreshed)
                    {
                        profile.State = UserState.Ready;
                        await _repo.SaveAsync(profile, ct);
                        MessageSender.SendWithRetry(chatId,
                            "¡Listo! Autorización completada ✅. Ya podés registrar cirugías.",
                            cancellationToken: ct);
                    }
                    else
                    {
                        MessageSender.SendWithRetry(chatId,
                            "Todavía no recibí la autorización. Asegurate de completar el flujo del enlace.",
                            cancellationToken: ct);
                    }
                }
                else
                {
                    MessageSender.SendWithRetry(chatId,
                        "Cuando termines la autorización en el navegador escribí 'ok'.",
                        cancellationToken: ct);
                }
                return (true, profile);

            case UserState.Ready:
                // Ya tratado más arriba
                return (false, profile);
        }

        return (false, profile);
    }

    private static bool EsComandoStart(string raw) =>
       raw.StartsWith("/start", StringComparison.OrdinalIgnoreCase);

    private static bool EsComandoAyuda(string lower) =>
        lower == "/ayuda" || lower == "ayuda";

    private static async Task EnviarMensajeBienvenidaAsync(
        ITelegramBotClient bot,
        long chatId,
        UserProfile profile,
        CancellationToken ct)
    {
        var texto = MensajeBienvenida();
        // Teclado para compartir teléfono si aún no lo tiene
        ReplyMarkup? reply = null;
        if (string.IsNullOrWhiteSpace(profile.Phone))
        {
            reply = new ReplyKeyboardMarkup(new[]
            {
                KeyboardButton.WithRequestContact("📱 Compartir mi teléfono")
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };
        }

        MessageSender.SendWithRetry(chatId, texto, replyMarkup: reply, cancellationToken: ct);
    }

    private static async Task PedirTelefonoAsync(
        ITelegramBotClient bot,
        long chatId,
        CancellationToken ct,
        bool mensajeInicial = false)
    {
        var prefix = mensajeInicial
            ? "¡Hola! Para comenzar,"
            : "Necesito tu número de teléfono para continuar,";

        var msg = $"{prefix} compartilo con el botón o escribilo (ej: +54911...).";
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            KeyboardButton.WithRequestContact("📱 Compartir mi teléfono")
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        MessageSender.SendWithRetry(chatId, msg, replyMarkup: replyKeyboard, cancellationToken: ct);
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

    private static string NormalizarTelefono(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;
        var filtered = new string(phone.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
        if (!filtered.StartsWith("+") && filtered.StartsWith("54"))
            filtered = "+" + filtered;
        return filtered;
    }

    private static bool IsValidEmail(string s) =>
        !string.IsNullOrWhiteSpace(s) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            s.Trim(),
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);


    private static string MensajeBienvenida() =>
@"¡Hola! 👋 Soy el asistente de registro de cirugías.

            Te ayudo a capturar rápidamente los datos de una cirugía y crear el evento en tu Google Calendar con invitación al anestesiólogo.

            🔐 *Primero necesito asociar tu cuenta*:
            1) Compartí tu número de teléfono (botón o escribilo).
            2) Decime tu email donde vas a registrar las cirugias.
            3) Autorizá el acceso al calendario del email.
            4) Campos que necesito: día, mes , hora, lugar, cirujano, cirugía, cantidad y anestesiólogo.

            🗓 *Luego enviá cirugías así*:
                    • ""07/08 Anchorena 14hs Falcone, Jorge septumplastia x2""
                    • ""Jueves 15 Gonzalez 8hs Sanatorio Mater Dei Mariano rinoplastia""
                    • ""25 Instituto del Callao, 8hs Lugano,  Pablito 2 septum""

                    🎤 *Audio ejemplo*:
                    “Jueves 7, Mater Dei, 16 horas, Quiroga, Francisco, dos casos de Amigdalas”.

                    Cuando estés listo, enviá tu número o usá el botón 📱.

                    Escribí *ayuda* o /ayuda para repetir estas instrucciones.";

    private static string MensajeAyuda() =>
@"Recordatorio de formatos:

            • ""07/08 Anchorena 14hs Falcone, Jorge septumplastia x2""
            • ""Jueves 15 Gonzalez 8hs Sanatorio Mater Dei Mariano rinoplastia""
            • ""25 Instituto del Callao, 8hs Lugano,  Pablito 2 septum""

            🎤 *Audio ejemplo*:
            “Jueves 7, Mater Dei, 16 horas, Quiroga, Francisco, dos casos de Amigdalas”.


            Campos que necesito: día, mes (si omitís se asume el próximo posible), hora, lugar, cirujano, cirugía, cantidad y anestesiólogo.

            Enviá tu número si todavía no lo hiciste, o tu email de Google si ya di ese paso.";

}
*/
