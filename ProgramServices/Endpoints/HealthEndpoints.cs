using System;
using Telegram.Bot;

namespace RegistroCx.ProgramServices.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Json(new 
        { 
            status = "OK", 
            timestamp = DateTime.UtcNow,
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            version = "1.0.0"
        }));

        app.MapGet("/health/bot", async (TelegramBotClient botClient) =>
        {
            try
            {
                var me = await botClient.GetMe();
                return Results.Ok(new { status = "healthy", botName = me.Username });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Bot unhealthy: {ex.Message}");
            }
        });
    }
}
