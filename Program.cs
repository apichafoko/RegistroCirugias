using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Requests;
using RegistroCx.Domain;
using RegistroCx.Services.Repositories;
using RegistroCx.Services.Onboarding;
using RegistroCx.Services.Extraction;
using RegistroCx.Services;
using RegistroCx.Helpers._0Auth;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Configuración de logging para producción
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.WebHost.UseUrls("http://0.0.0.0:5002", "https://0.0.0.0:5003");

var app = builder.Build();

// Obtener logger
var logger = app.Services.GetRequiredService<ILogger<Program>>();

string Env(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"Falta variable de entorno: {key}");

// Cargar variables de entorno
try
{
    DotNetEnv.Env.Load();
    logger.LogInformation("Variables de entorno cargadas correctamente");
}
catch (Exception ex)
{
    logger.LogWarning("No se pudo cargar archivo .env: {Message}", ex.Message);
}

var botToken = Env("TELEGRAM_BOT_TOKEN");
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy";
var assistantId = Environment.GetEnvironmentVariable("OPENAI_ASSISTANT_ID") ?? "assistant_dummy";
var connString = Environment.GetEnvironmentVariable("DB_CONN") ?? "assistant_dummy";
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

var googleOpts = new GoogleOAuthOptions
{
    ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")!,
    ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")!,
    RedirectUri = Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")!
};

/// Configurar HttpClient para Telegram de forma segura
var httpClientHandler = new HttpClientHandler()
{
    // Configuraciones de seguridad
    CheckCertificateRevocationList = true,
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
    
    // Configuraciones de conexión
    //MaxConnectionsPerServer = 20,
    
    // Timeouts a nivel de handler
    ServerCertificateCustomValidationCallback = null // Usar validación por defecto
};

var httpClient = new HttpClient(httpClientHandler)
{
    // Timeout principal - importante para evitar conexiones colgadas
    Timeout = TimeSpan.FromSeconds(30),
    
    // Headers por defecto
    DefaultRequestHeaders = 
    {
        {"User-Agent", "RegistroCx-Bot/1.0"}
    }
};

// Configurar timeouts más específicos si es necesario
httpClient.DefaultRequestHeaders.ConnectionClose = false; // Reutilizar conexiones


// Solo en desarrollo permitir certificados no válidos
if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
{
    logger.LogWarning("ENTORNO DE DESARROLLO: Deshabilitando validación SSL");
    httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
}
else
{
    // En producción, configuración SSL segura
    httpClientHandler.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    httpClientHandler.CheckCertificateRevocationList = true;
    
    // Callback personalizado para logging de problemas SSL en producción
    httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
    {
        if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
            return true;

        logger.LogError("Error de certificado SSL: {Errors}", sslPolicyErrors);
        
        // En producción, rechazar conexiones inseguras
        return false;
    };
}

//var httpClient = new HttpClient(httpClientHandler)
//{
    //Timeout = TimeSpan.FromSeconds(30)
//};

// Agregar User-Agent
httpClient.DefaultRequestHeaders.Add("User-Agent", "RegistroCx-Bot/1.0");

var botClient = new TelegramBotClient(botToken, httpClient);

logger.LogInformation("Bot client configurado para entorno: {Environment}", environment);

// Servicios
var serviceHttp = new HttpClient(); // HttpClient separado para otros servicios
var userRepo = new UserProfileRepository(connString);
var googleOauth = new GoogleOAuthService(googleOpts, serviceHttp, userRepo);
var onboarding = new OnboardingService(userRepo, googleOauth);
var llm = new LLMOpenAIAssistant(openAiKey);
var cirugiaFlow = new CirugiaFlowService(llm);

// Mapeo callback OAuth
app.MapGet("/oauth/google/callback", async (HttpRequest req) =>
{
    try
    {
        var code = req.Query["code"].ToString();
        var state = req.Query["state"].ToString();
        
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            logger.LogWarning("OAuth callback con parámetros inválidos");
            return Results.Text("Parámetros inválidos.");
        }

        var parts = state.Split(':');
        if (!long.TryParse(parts[0], out var chatId))
        {
            logger.LogWarning("State inválido en OAuth callback: {State}", state);
            return Results.Text("State inválido.");
        }

        logger.LogInformation("Procesando OAuth callback para chat {ChatId}", chatId);

        var token = await googleOauth.ExchangeCodeAsync(code, state, CancellationToken.None);
        if (token == null)
        {
            logger.LogError("Error intercambiando código OAuth para chat {ChatId}", chatId);
            return Results.Text("Error autorizando. Volvé al bot e intentá de nuevo.");
        }

        await botClient.SendMessage(chatId, "✅ Autorización completada. Ya podés enviar cirugías.", cancellationToken: CancellationToken.None);
        logger.LogInformation("OAuth completado exitosamente para chat {ChatId}", chatId);

        return Results.Text("¡Listo! Podés volver al bot.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error en OAuth callback");
        return Results.Text("Error interno. Contactá al administrador.");
    }
});

// Health endpoint con más información
app.MapGet("/health", () => 
{
    return Results.Json(new 
    { 
        status = "OK", 
        timestamp = DateTime.UtcNow,
        environment = environment,
        version = "1.0.0"
    });
});

// Configuración de polling con manejo robusto de errores
var cts = new CancellationTokenSource();

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("Iniciando shutdown graceful...");
    cts.Cancel();
});

// Iniciar aplicación web en segundo plano
var webTask = Task.Run(async () =>
{
    try
    {
        await app.RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Aplicación web detenida");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error en aplicación web");
    }
});

// Delay inicial
await Task.Delay(2000, cts.Token);

logger.LogInformation("Iniciando bot de Telegram...");

// Verificar conectividad inicial
try
{
    var me = await botClient.GetMe(cts.Token);
    logger.LogInformation("Bot conectado exitosamente: @{Username} ({Id})", me.Username, me.Id);
}
catch (Exception ex)
{
    logger.LogError(ex, "Error verificando conectividad del bot");
    
    if (ex.Message.Contains("UntrustedRoot") || ex.Message.Contains("SSL"))
    {
        logger.LogError("PROBLEMA SSL DETECTADO - Verifica los certificados del sistema");
        logger.LogError("Para solucionar, ejecuta: sudo apt-get update && sudo apt-get install ca-certificates");
    }
    
    Environment.Exit(1);
}

// Configuración de retry con backoff exponencial
var retryCount = 0;
var maxRetries = 5;
var baseDelay = TimeSpan.FromSeconds(5);

while (retryCount < maxRetries && !cts.Token.IsCancellationRequested)
{
    try
    {
        logger.LogInformation("Intento {Retry}/{MaxRetries} de iniciar polling", retryCount + 1, maxRetries);
        
        botClient.StartReceiving(
            async (bot, update, ct) =>
            {
                try
                {
                    if (update.Message is not { } msg) return;
                    var chatId = msg.Chat.Id;
                    var text = msg.Text ?? "";
                    var phoneFromContact = msg.Contact?.PhoneNumber;

                    logger.LogDebug("Mensaje recibido de chat {ChatId}: {Text}", chatId, text);

                    // Onboarding
                    var (handled, profile) = await onboarding.HandleAsync(bot, chatId, text, phoneFromContact, ct);
                    if (handled) return;
                    if (profile.State != UserState.Ready) return;

                    // Procesar flujo de cirugía
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        logger.LogInformation("Iniciando procesamiento de cirugía para chat {ChatId}", chatId);
                        
                        try
                        {
                            await bot.SendMessage(chatId, "🔄 Procesando...", cancellationToken: ct);
                            await cirugiaFlow.HandleAsync(bot, chatId, text, ct);
                            logger.LogInformation("Procesamiento completado para chat {ChatId}", chatId);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error procesando cirugía para chat {ChatId}", chatId);
                            try
                            {
                                await bot.SendMessage(chatId, "❌ Error procesando la solicitud. Intentá de nuevo.", cancellationToken: ct);
                            }
                            catch (Exception sendEx)
                            {
                                logger.LogError(sendEx, "Error enviando mensaje de error a chat {ChatId}", chatId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error procesando update");
                }
            },
            (bot, ex, ct) =>
            {
                logger.LogError(ex, "Error de polling");

                // Detectar errores específicos
                if (ex is ApiRequestException { ErrorCode: 409 })
                {
                    logger.LogWarning("Error 409: Otra instancia del bot está corriendo");
                }
                else if (ex.Message.Contains("SSL") || ex.Message.Contains("certificate"))
                {
                    logger.LogError("Error SSL detectado - verifica certificados del sistema");
                }
                else if (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
                {
                    logger.LogWarning("Timeout detectado - reintentando...");
                }

                return Task.CompletedTask;
            },
            cancellationToken: cts.Token
        );
        
        logger.LogInformation("Bot iniciado correctamente");
        retryCount = 0; // Reset contador en caso de éxito
        break;
    }
    catch (Exception ex) when (ex is ApiRequestException { ErrorCode: 409 })
    {
        retryCount++;
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
        
        logger.LogWarning("Error 409 al iniciar. Reintento {Retry}/{MaxRetries} en {Delay}s", 
            retryCount, maxRetries, delay.TotalSeconds);
        
        if (retryCount < maxRetries)
        {
            await Task.Delay(delay, cts.Token);
        }
        else
        {
            logger.LogError("Máximo número de reintentos alcanzado para error 409");
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error inesperado al iniciar bot");
        Environment.Exit(1);
    }
}

logger.LogInformation("Bot en funcionamiento. Presiona Ctrl+C para detener");

// Manejo de señales del sistema
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    logger.LogInformation("Señal de interrupción recibida, iniciando shutdown...");
    cts.Cancel();
};

// Esperar hasta cancelación
try
{
    await Task.Delay(-1, cts.Token);
}
catch (OperationCanceledException)
{
    logger.LogInformation("Aplicación detenida por cancelación");
}

// Cleanup
try
{
    httpClient?.Dispose();
    serviceHttp?.Dispose();
    logger.LogInformation("Cleanup completado");
}
catch (Exception ex)
{
    logger.LogError(ex, "Error durante cleanup");
}