using System;
using RegistroCx.Helpers._0Auth;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services.Onboarding;
using RegistroCx.Services;
using Telegram.Bot;

namespace RegistroCx.ProgramServices.Endpoints;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this WebApplication app)
    {
        // Endpoint simple de test
        app.MapGet("/oauth/test", () => "OAuth endpoint working!");
        
        app.MapGet("/oauth/google/callback", async (
            HttpRequest req,
            IGoogleOAuthService googleOauth,
            TelegramBotClient botClient,
            CalendarSyncService calendarSync,
            ILogger<Program> logger) =>
        {
            try
            {
                logger.LogInformation("[OAUTH-CALLBACK] Received callback request");
                logger.LogInformation("[OAUTH-CALLBACK] Query parameters: {QueryString}", req.QueryString);
                
                var code = req.Query["code"].ToString();
                var state = req.Query["state"].ToString();
                
                logger.LogInformation("[OAUTH-CALLBACK] Code: {Code}, State: {State}", 
                    string.IsNullOrEmpty(code) ? "MISSING" : "PRESENT", 
                    string.IsNullOrEmpty(state) ? "MISSING" : state);
                
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    return Results.Text("Parámetros inválidos.");
                }

                var parts = state.Split(':');
                if (!long.TryParse(parts[0], out var chatId))
                {
                    return Results.Text("State inválido.");
                }

                var token = await googleOauth.ExchangeCodeAsync(code, state, CancellationToken.None);
                if (token == null)
                {
                    return Results.Text("Error autorizando. Volvé al bot e intentá de nuevo.");
                }

                // Iniciar sincronización de calendarios en segundo plano
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000); // Pequeña espera para asegurar que los tokens se guardaron
                        
                        var syncedCount = await calendarSync.SyncPendingAppointmentsAsync(botClient, chatId, CancellationToken.None);
                        
                        if (syncedCount > 0)
                        {
                            await MessageSender.SendWithRetry(chatId, 
                                "🎉 ¡Autorización completada y appointments sincronizados exitosamente!");
                        }
                        else
                        {
                            await MessageSender.SendWithRetry(chatId, 
                                "✅ Autorización completada. Ya podés crear eventos en tu calendario.");
                        }
                    }
                    catch (Exception syncEx)
                    {
                        logger.LogError(syncEx, "Error durante sincronización post-OAuth");
                        await MessageSender.SendWithRetry(chatId, 
                            "✅ Autorización completada, pero hubo un problema con la sincronización. Escribe /autorizar si necesitas sincronizar appointments pendientes.");
                    }
                });
                
                return Results.Text("¡Listo! La autorización fue exitosa. Volvé al bot para ver los resultados.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en OAuth callback");
                return Results.Text("Error interno. Contactá al administrador.");
            }
        });
    }
}
