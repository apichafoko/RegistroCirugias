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
using Microsoft.Extensions.Logging;

namespace RegistroCx.Services.Onboarding
{
    public class OnboardingService : IOnboardingService
    {
        private readonly IUserProfileRepository _repo;
        private readonly IUsuarioTelegramRepository _telegramRepo;
        private readonly IGoogleOAuthService _oauth;
        private readonly ILogger<OnboardingService> _logger;

        public OnboardingService(
            IUserProfileRepository repo,
            IUsuarioTelegramRepository telegramRepo,
            IGoogleOAuthService oauth,
            ILogger<OnboardingService> logger)
        {
            _repo = repo;
            _telegramRepo = telegramRepo;
            _oauth = oauth;
            _logger = logger;
        }

        public async Task<(bool handled, UserProfile profile)> HandleAsync(
            ITelegramBotClient bot,
            long chatId,
            string rawText,
            string? phoneFromContact,
            long? telegramUserId,
            string? firstName,
            string? lastName,
            string? username,
            string? languageCode,
            CancellationToken ct)
        {
            rawText ??= "";
            var lower = rawText.Trim().ToLowerInvariant();
            
            _logger.LogInformation("[ONBOARDING-ENTRY] chatId: {chatId}, rawText: {rawText}, phoneFromContact: {phone}, telegramUserId: {telegramId}", 
                chatId, rawText, phoneFromContact, telegramUserId);
            
            // Si hay teléfono compartido, buscar perfil existente o crear uno nuevo
            UserProfile? profile = null;
            if (!string.IsNullOrWhiteSpace(phoneFromContact))
            {
                var normalizedPhone = NormalizarTelefono(phoneFromContact);
                var existingProfile = await _repo.FindByPhoneAsync(normalizedPhone, ct);
                if (existingProfile != null)
                {
                    _logger.LogInformation("[ONBOARDING] Found existing profile by phone: {profileId}, currentChatId: {currentChatId}", 
                        existingProfile.Id, existingProfile.ChatId);
                    
                    // Actualizar chat_id si es diferente
                    if (existingProfile.ChatId != chatId)
                    {
                        await _repo.LinkChatIdByIdAsync(existingProfile.Id, chatId, ct);
                        existingProfile.ChatId = chatId; // Actualizar el objeto en memoria también
                        _logger.LogInformation("[ONBOARDING] Updated profile chat_id to: {chatId}", chatId);
                    }
                    
                    profile = existingProfile;
                }
                else
                {
                    _logger.LogError("[ONBOARDING] No existing profile found for phone: {phone}. All users should exist beforehand.", normalizedPhone);
                    // Como todos los usuarios deben existir de antemano, esto es un error
                    await MessageSender.SendWithRetry(chatId,
                        "Lo siento, no puedo encontrar tu perfil en el sistema. Por favor contacta al administrador.",
                        cancellationToken: ct);
                    return (true, new UserProfile { ChatId = chatId, State = UserState.NeedPhone });
                }
                
                // Asegurar que el perfil tenga el teléfono correcto
                //if (profile.Phone != normalizedPhone)
                //{
                    //profile.Phone = normalizedPhone;
                    //await _repo.SaveAsync(profile, ct);
                    //_logger.LogInformation("[ONBOARDING] Updated profile phone to: {phone}", normalizedPhone);
                //}
                
                // Actualizar datos de Telegram solo cuando hay teléfono compartido
                await _telegramRepo.UpdateTelegramDataByPhoneAsync(chatId, telegramUserId, firstName, username, normalizedPhone, ct);
            }
            else
            {
                // Sin teléfono compartido, intentar obtener perfil existente por chatId
                profile = await _repo.GetAsync(chatId, ct);
                _logger.LogInformation("[ONBOARDING] Existing profile by chatId found: {found}", profile != null);
            }

            // /start, /ayuda o ayuda (incluyendo variaciones con errores tipográficos)
            if (lower == "/start" || lower == "/ayuda" || lower == "ayuda" || IsSimilarToHelp(lower))
            {
                if (profile != null && profile.State == UserState.Ready)
                {
                    await EnviarBienvenida(bot, chatId, ct);
                    return (true, profile);
                }
                
                // Para /start sin perfil existente, solo enviar bienvenida (pedir teléfono)
                await EnviarBienvenida(bot, chatId, ct);
                
                // Si hay perfil, devolverlo, sino devolver un perfil temporal para el tipo de retorno
                if (profile != null)
                {
                    return (true, profile);
                }
                else
                {
                    // Crear perfil temporal solo para el retorno, no guardarlo en BD
                    var tempProfile = new UserProfile { ChatId = chatId, State = UserState.NeedPhone };
                    return (true, tempProfile);
                }
            }

            // compartir teléfono
            if (!string.IsNullOrWhiteSpace(phoneFromContact))
            {
                var normalizedPhone = NormalizarTelefono(phoneFromContact);
                _logger.LogInformation("[PHONE-SHARING] Processing phone: {phone}, normalized: {normalizedPhone}", phoneFromContact, normalizedPhone);
                
                // Asegurar que el perfil tenga el teléfono correcto
                if (profile != null && profile.Phone != normalizedPhone)
                {
                    profile.Phone = normalizedPhone;
                    await _repo.SaveAsync(profile, ct);
                    _logger.LogInformation("[PHONE-SHARING] Updated profile phone to: {phone}", normalizedPhone);
                }
                
                // Actualizar usuarios_telegram buscando por teléfono primero
                await _telegramRepo.UpdateTelegramDataByPhoneAsync(chatId, telegramUserId, firstName, username, normalizedPhone, ct);
                
                // Como todos los usuarios ya existen con teléfono y email, verificar el estado
                if (profile != null && profile.State == UserState.Ready)
                {
                    await MessageSender.SendWithRetry(chatId,
                        $"¡Hola {await GetTelegramDisplayName(profile.ChatId ?? chatId, ct)}! 👋\n\n" +
                        "Te reconocí por tu teléfono. ¡Qué bueno que estés acá!\n\n" +
                        "Tu perfil ya está configurado. Podés empezar a enviarme cirugías directamente.",
                        replyMarkup: new ReplyKeyboardRemove(),
                        cancellationToken: ct);
                }
                else
                {
                    // Usuario preexistente necesita autorizar calendario
                    var displayName = await GetTelegramDisplayName(profile!.ChatId ?? chatId, ct);
                    profile.State = UserState.NeedOAuth;
                    await _repo.SaveAsync(profile, ct);
                    
                    await MessageSender.SendWithRetry(chatId,
                        $"¡Hola {displayName}! 👋 ¿Cómo estás?\n\n" +
                        $"Qué bueno verte por acá. Necesitamos que autorices el calendario de tu email <b>{profile.GoogleEmail}</b> para poder seguir adelante.\n\n" +
                        "Escribí <b>continuar</b> para generar el enlace de autorización.",
                        cancellationToken: ct);
                }
                
                return (true, profile);
            }

            // Si no hay perfil válido, no se puede procesar más comandos
            if (profile == null)
            {
                _logger.LogInformation("[ONBOARDING] No profile available, user needs to share phone first");
                return (false, new UserProfile { ChatId = chatId, State = UserState.NeedPhone });
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
                        // TELÉFONO ES 1:1 - Verificar si ya existe un usuario con ese teléfono
                        var existingProfile = await _repo.FindByPhoneAsync(phoneManual, ct);
                        if (existingProfile != null && existingProfile.ChatId != chatId)
                        {
                            // Teléfono único: actualizar ChatId del perfil existente y datos de Telegram
                            await _repo.LinkChatIdByIdAsync(existingProfile.Id, chatId, ct);
                            existingProfile.ChatId = chatId;
                            await UpdateTelegramData(existingProfile, telegramUserId, firstName, lastName, username, languageCode, ct);
                            
                            await MessageSender.SendWithRetry(chatId,
                                $"¡Hola {await GetTelegramDisplayName(existingProfile.ChatId ?? chatId, ct)}! 👋\n\n" +
                                "Te reconocí por tu teléfono. ¡Qué bueno que estés acá!\n\n" +
                                "Tu perfil ya está configurado. Podés empezar a enviarme cirugías directamente.",
                                replyMarkup: new ReplyKeyboardRemove(),
                                cancellationToken: ct);
                            return (true, existingProfile);
                        }
                        
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
                        var email = rawText.Trim();
                        
                        // EMAIL ES 1:N - Verificar si ya existe un usuario con ese email de equipo
                        var existingProfile = await _repo.FindByEmailAsync(email, ct);
                        if (existingProfile != null && existingProfile.ChatId != chatId)
                        {
                            // Email compartido: crear nuevo registro copiando tokens OAuth
                            var newProfile = await _repo.CreateProfileCopyingEmailTokensAsync(existingProfile, chatId, ct);
                            await UpdateTelegramData(newProfile, telegramUserId, firstName, lastName, username, languageCode, ct);
                            
                            await MessageSender.SendWithRetry(chatId,
                                $"¡Hola {await GetTelegramDisplayName(newProfile.ChatId ?? chatId, ct)}! 👋\n\n" +
                                "Te reconocí por tu email de equipo. ¡Qué bueno que estés acá!\n\n" +
                                "Tu perfil ya está configurado con acceso al calendario compartido. Podés empezar a enviarme cirugías directamente.",
                                replyMarkup: new ReplyKeyboardRemove(),
                                cancellationToken: ct);
                            return (true, newProfile);
                        }
                        
                        profile.GoogleEmail = email;
                        profile.State = UserState.NeedOAuth;
                        await _repo.SaveAsync(profile, ct);
                        await MessageSender.SendWithRetry(chatId,
                            "Genial ✅. Escribí <b>continuar</b> para autorizar Calendar.",
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
                            $"<a href=\"{url}\">Abri este enlace para autorizar</a>\n\nLuego escribí <b>ok</b>.",
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId,
                            "Escribí <b>continuar</b> para generar el enlace.",
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
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: ct);
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId,
                            "Cuando autorices, escribí <b>ok</b>.",
                            cancellationToken: ct);
                    }
                    return (true, profile);
            }

            return (false, profile);
        }

        private async Task EnviarBienvenida(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var profile = await _repo.GetAsync(chatId, ct);

            if (profile?.State == UserState.Ready)
            {
            var txt =
    @"¡Hola! 👋 Soy tu asistente inteligente para registrar cirugías.

📋 <b>¿CÓMO FUNCIONA?</b>
Simplemente escribime los datos de tu cirugía en lenguaje natural. Yo entiendo y organizo automáticamente:

🔹 <b>Ejemplo:</b> ""23/08 2 CERS + 1 MLD quiroga ancho uri 14hs""
• Detectaré que son 3 cirugías diferentes
• Extraeré fecha, hora, lugar, cirujano, etc.
• Te pediré solo los datos que falten
• Crearé eventos en tu Google Calendar

✨ <b>CARACTERÍSTICAS:</b>
• 🎤 Acepto mensajes de voz
• 🔢 Proceso múltiples cirugías de una vez
• 📅 Sincronización automática con Google Calendar
• 💉 Invito anestesiólogos por email
• ⚡ Edición granular (""cirugía 1 hora 16hs"")

📊 <b>REPORTES:</b>
• **/semanal** - Resumen de esta semana
• **/mensual** - Resumen del último mes

🚀 <b>¡Empezá ahora!</b> Mandame cualquier cirugía y yo me encargo del resto.";
            // Remover teclado cuando el usuario ya está configurado
            await MessageSender.SendWithRetry(chatId, txt, replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
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

        /// <summary>
        /// Obtiene o crea un perfil de usuario simplificado:
        /// 1. Busca por chat_id
        /// 2. Si no encuentra, crea nuevo perfil
        /// Nota: La lógica de vinculación por teléfono se maneja directamente en el flujo de sharing
        /// </summary>
        private async Task<UserProfile> GetOrCreateProfileSmartAsync(long chatId, string? phoneFromContact, long? telegramUserId, CancellationToken ct)
        {
            _logger.LogInformation("[GET-OR-CREATE-START] chatId: {chatId}", chatId);
                
            // 1. Intentar buscar por chat_id primero
            var profile = await _repo.GetAsync(chatId, ct);
            _logger.LogInformation("[GET-OR-CREATE] Profile by chatId found: {found}", profile != null);
            if (profile != null)
            {
                return profile;
            }

            // 2. No existe, crear nuevo perfil
            _logger.LogInformation("[GET-OR-CREATE] Creating new profile for chatId: {chatId}", chatId);
            return await _repo.GetOrCreateAsync(chatId, ct);
        }

        /// <summary>
        /// Actualiza los datos de Telegram del perfil
        /// </summary>
        private async Task UpdateTelegramData(UserProfile profile, long? telegramUserId, string? firstName, string? lastName, string? username, string? languageCode, CancellationToken ct)
        {
            if (telegramUserId.HasValue)
            {
                await _telegramRepo.UpdateTelegramDataAsync(
                    profile.ChatId ?? 0,
                    telegramUserId.Value,
                    firstName,
                    username,
                    profile.Phone, // ✅ Incluir teléfono del perfil
                    ct: ct);
            }
        }

        /// <summary>
        /// Obtiene el nombre de display de Telegram para un usuario
        /// </summary>
        private async Task<string> GetTelegramDisplayName(long chatId, CancellationToken ct)
        {
            var telegramUser = await _telegramRepo.GetByChatIdAsync(chatId, ct);
            return telegramUser?.GetDisplayName() ?? "Usuario";
        }

        /// <summary>
        /// Detecta si el texto es similar a "ayuda" con errores tipográficos comunes
        /// </summary>
        private static bool IsSimilarToHelp(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;

            var normalized = input.Trim().ToLowerInvariant();
            
            // Patrones comunes de errores tipográficos para "ayuda"
            var helpPatterns = new[]
            {
                "aayyda",    // Error reportado por el usuario
                "ayyda",     // Falta una 'a'
                "ayda",      // Faltan letras
                "auda",      // Falta 'y'
                "ayuuda",    // Doble 'u'
                "ayudda",    // Doble 'd'
                "hayuda",    // 'h' extra
                "ajuda",     // 'j' en lugar de 'y'
                "alluda",    // Confusión de teclas
                "help",      // En inglés
                "helpme",    // En inglés
            };

            return helpPatterns.Any(pattern => 
                normalized == pattern || 
                (normalized.Length >= 3 && CalculateLevenshteinDistance(normalized, "ayuda") <= 2));
        }

        /// <summary>
        /// Calcula la distancia de Levenshtein entre dos strings para detectar similitudes
        /// </summary>
        private static int CalculateLevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            
            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;

            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 1; i <= sourceLength; i++)
                distance[i, 0] = i;

            for (int j = 1; j <= targetLength; j++)
                distance[0, j] = j;

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }
    }
}
