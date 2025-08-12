using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services.Onboarding;
using RegistroCx.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RegistroCx.ProgramServices.Services.Telegram;

public class TelegramBotService : BackgroundService
{
    private readonly TelegramBotClient _bot;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ReceiverOptions _receiverOptions;
    private readonly IServiceProvider _serviceProvider;

    public TelegramBotService(TelegramBotClient bot, ILogger<TelegramBotService> logger, IServiceProvider serviceProvider)
    {
        _bot = bot;
        _logger = logger;
        _serviceProvider = serviceProvider;
        
        // Configuración robusta para el polling
        _receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>(), // Recibir todos los tipos de updates
            Limit = 100 // Procesar hasta 100 updates por vez
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bot iniciado correctamente");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Configurar HttpClient con timeouts más largos
                var httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromMinutes(5) // 5 minutos en lugar de 30 segundos
                };

                var botClient = new TelegramBotClient(_bot.Token, httpClient);
                MessageSender.Bot = botClient; // Actualizar la referencia estática

                _logger.LogInformation("Iniciando polling...");

                // Usar polling con manejo robusto de errores
                await botClient.ReceiveAsync(
                    updateHandler: HandleUpdate,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: _receiverOptions,
                    cancellationToken: stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Bot detenido por solicitud de cancelación");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico en el polling. Reintentando en 10 segundos...");
                
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Servicio de bot detenido");
    }

    private async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Message is { } message)
            {
                // Handle text messages
                if (!string.IsNullOrWhiteSpace(message.Text))
                {
                    _logger.LogInformation("Mensaje recibido de {Username}: {Text}",
                        message.From?.Username ?? "Usuario desconocido",
                        message.Text);

                    await HandleMessageAsync(botClient, message, cancellationToken);
                }
                // Handle voice messages
                else if (message.Voice != null)
                {
                    _logger.LogInformation("Mensaje de voz recibido de {Username}: {Duration}s",
                        message.From?.Username ?? "Usuario desconocido",
                        message.Voice.Duration);

                    await HandleVoiceMessageAsync(botClient, message, cancellationToken);
                }
                // Handle audio messages
                else if (message.Audio != null)
                {
                    _logger.LogInformation("Archivo de audio recibido de {Username}: {Duration}s",
                        message.From?.Username ?? "Usuario desconocido",
                        message.Audio.Duration);

                    await HandleAudioMessageAsync(botClient, message, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al procesar update");
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var onboardingService = scope.ServiceProvider.GetRequiredService<IOnboardingService>();
        var cirugiaFlowService = scope.ServiceProvider.GetRequiredService<CirugiaFlowService>();

        var chatId = message.Chat.Id;
        var text = message.Text!;

        // Extraer teléfono del contacto si existe
        var phoneFromContact = message.Contact?.PhoneNumber;
        
        // Obtener información completa del usuario de Telegram
        var telegramUser = message.From;
        var telegramUserId = telegramUser?.Id;
        var firstName = telegramUser?.FirstName;
        var lastName = telegramUser?.LastName;
        var username = telegramUser?.Username;
        var languageCode = telegramUser?.LanguageCode;
        
        // Primero verificar onboarding y validaciones de usuario
        var result = await onboardingService.HandleAsync(botClient, chatId, text, phoneFromContact, telegramUserId, firstName, lastName, username, languageCode, cancellationToken);
        var handled = result.handled;
        var userProfile = result.profile;
        
        if (handled)
        {
            // El onboarding manejó el mensaje (usuario nuevo o incompleto)
            return;
        }

        // Usuario válido, procesar con CirugiaFlowService
        await cirugiaFlowService.HandleAsync(botClient, chatId, text, cancellationToken);
    }

    private async Task HandleVoiceMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var audioService = scope.ServiceProvider.GetRequiredService<AudioTranscriptionService>();
        
        var chatId = message.Chat.Id;
        
        // Notify user that we're processing the voice message
        await MessageSender.SendWithRetry(chatId, 
            "🎤 Procesando mensaje de voz...", 
            cancellationToken: cancellationToken);

        try
        {
            // Transcribe the voice message
            var transcribedText = await audioService.TranscribeVoiceAsync(botClient, message.Voice!, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                await MessageSender.SendWithRetry(chatId,
                    "❌ No pude entender el mensaje de voz. Por favor, intenta nuevamente o escribe el mensaje.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Show what was transcribed
            await MessageSender.SendWithRetry(chatId,
                $"🎤➡️📝 Entendí: \"{transcribedText}\"",
                cancellationToken: cancellationToken);

            // Process the transcribed text through normal flow
            await ProcessTranscribedText(botClient, message, transcribedText, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice message");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando el mensaje de voz. Por favor, intenta nuevamente.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleAudioMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var audioService = scope.ServiceProvider.GetRequiredService<AudioTranscriptionService>();
        
        var chatId = message.Chat.Id;
        
        // Notify user that we're processing the audio file
        await MessageSender.SendWithRetry(chatId, 
            "🎵 Procesando archivo de audio...", 
            cancellationToken: cancellationToken);

        try
        {
            // Transcribe the audio file
            var transcribedText = await audioService.TranscribeAudioAsync(botClient, message.Audio!, cancellationToken);
            
            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                await MessageSender.SendWithRetry(chatId,
                    "❌ No pude entender el archivo de audio. Por favor, intenta nuevamente o escribe el mensaje.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Show what was transcribed
            await MessageSender.SendWithRetry(chatId,
                $"🎵➡️📝 Entendí: \"{transcribedText}\"",
                cancellationToken: cancellationToken);

            // Process the transcribed text through normal flow
            await ProcessTranscribedText(botClient, message, transcribedText, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio message");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando el archivo de audio. Por favor, intenta nuevamente.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task ProcessTranscribedText(ITelegramBotClient botClient, Message originalMessage, string transcribedText, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var onboardingService = scope.ServiceProvider.GetRequiredService<IOnboardingService>();
        var cirugiaFlowService = scope.ServiceProvider.GetRequiredService<CirugiaFlowService>();

        var chatId = originalMessage.Chat.Id;

        // Extraer teléfono del contacto si existe (aunque es poco probable en audio)
        var phoneFromContact = originalMessage.Contact?.PhoneNumber;
        
        // Primero verificar onboarding y validaciones de usuario
        var result = await onboardingService.HandleAsync(botClient, chatId, transcribedText, phoneFromContact, null, null, null, null, null, cancellationToken);
        var handled = result.handled;
        var userProfile = result.profile;
        
        if (handled)
        {
            // El onboarding manejó el mensaje (usuario nuevo o incompleto)
            return;
        }

        // Usuario válido, procesar con CirugiaFlowService
        await cirugiaFlowService.HandleAsync(botClient, chatId, transcribedText, cancellationToken);
    }

    private async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => 
                $"Error de API de Telegram:\n[{apiRequestException.ErrorCode}] {apiRequestException.Message}",
            RequestException requestException when requestException.Message.Contains("timed out") => 
                "Timeout de conexión - esto es normal en long polling",
            RequestException requestException => 
                $"Error de request: {requestException.Message}",
            HttpRequestException httpRequestException => 
                $"Error de HTTP: {httpRequestException.Message}",
            TaskCanceledException => 
                "Request cancelado - reconectando...",
            _ => exception.ToString()
        };

        // Solo loggear como error si no es un timeout normal
        if (exception is RequestException && exception.Message.Contains("timed out"))
        {
            _logger.LogDebug("Timeout normal de polling - reconectando...");
        }
        else
        {
            _logger.LogWarning("Error de polling: {ErrorMessage}", errorMessage);
        }

        // Esperar un poco antes de reintentar si no es un timeout normal
        if (!(exception is RequestException && exception.Message.Contains("timed out")))
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignorar si se cancela durante el delay
            }
        }
    }
}

// También necesitas configurar esto en Program.cs
public static class ProgramExtensions
{
    public static IHostBuilder ConfigureTelegramBot(this IHostBuilder hostBuilder, string botToken)
    {
        return hostBuilder.ConfigureServices((context, services) =>
        {
            // Configurar HttpClient con settings robustos
            services.AddHttpClient("telegram_bot_client")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
                .ConfigureHttpClient(client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                });

            // Registrar el bot
            services.AddSingleton<TelegramBotClient>(provider =>
            {
                var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient("telegram_bot_client");
                return new TelegramBotClient(botToken, httpClient);
            });

            // Registrar el servicio de background
            services.AddHostedService<TelegramBotService>();
        });
    }
}