using System;
using RegistroCx.Helpers._0Auth;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services.Onboarding;
using Telegram.Bot;

namespace RegistroCx.ProgramServices.Endpoints;

public static class OAuthEndpoints
{
    public static void MapOAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/oauth/google/callback", async (
            HttpRequest req,
            GoogleOAuthService googleOauth,
            TelegramBotClient botClient,
            ILogger<Program> logger) =>
        {
            try
            {
                var code = req.Query["code"].ToString();
                var state = req.Query["state"].ToString();
                
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

                await MessageSender.SendWithRetry(chatId, "✅ Autorización completada. Ya podés enviar cirugías.");
                
                return Results.Text("¡Listo! Podés volver al bot.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en OAuth callback");
                return Results.Text("Error interno. Contactá al administrador.");
            }
        });
    }
}
