using System;
using RegistroCx.ProgramServices.Services.Telegram;
using Telegram.Bot;
using RegistroCx.ProgramServices.Endpoints;

namespace RegistroCx.ProgramServices.Extensions;

public static class WebApplicationExtensions
{
    public static void ConfigureEndpoints(this WebApplication app)
    {
        app.MapOAuthEndpoints();
        app.MapHealthEndpoints();
    }

    public static async Task ConfigureTelegramBot(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var botClient = app.Services.GetRequiredService<TelegramBotClient>();
        
        // Asignar bot estático
        MessageSender.Bot = botClient;
        
        // Verificar conectividad
        try
        {
            var me = await botClient.GetMe();
            logger.LogInformation("Bot conectado: @{Username} ({Id})", me.Username, me.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verificando conectividad del bot");
            
            if (ex.Message.Contains("SSL"))
            {
                logger.LogError("PROBLEMA SSL - Ejecuta: sudo apt-get update && sudo apt-get install ca-certificates");
            }
            
            Environment.Exit(1);
        }
    }
}
