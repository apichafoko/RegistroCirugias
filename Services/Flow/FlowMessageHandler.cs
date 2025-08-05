using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Helpers;

namespace RegistroCx.Services.Flow;

public class FlowMessageHandler
{
    public async Task<bool> HandleSpecialCommands(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        var textLower = rawText.Trim().ToLowerInvariant();
        if (textLower is "/start" or "/reset" or "/reiniciar" or "reiniciar" or "cancelar" or "empezar de nuevo")
        {
            await bot.SendMessage(chatId,
                "✨ Perfecto, empezamos de nuevo. Contame los datos de la cirugía que querés agendar.",
                cancellationToken: ct);
            return true;
        }
        return false;
    }

    public async Task<bool> HandleConfirmation(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        if (!appt.ConfirmacionPendiente) return false;

        var lower = rawText.Trim().ToLowerInvariant();
        
        if (lower is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar")
        {
            appt.ConfirmacionPendiente = false;
            
            var (okFecha, err) = FechasHelper.ValidarFechaCirugia(appt.FechaHora, DateTime.Now);
            if (!okFecha)
            {
                await bot.SendMessage(chatId, err!, cancellationToken: ct);
                return true;
            }

            await bot.SendMessage(chatId,
                "✅ Confirmado. El evento fue creado en el calendario.",
                cancellationToken: ct);
            return true;
        }
        else if (lower.StartsWith("no") || lower.Contains("cambiar") || lower.Contains("correg"))
        {
            appt.ConfirmacionPendiente = false;
            appt.CampoAEditar = Appointment.CampoPendiente.EsperandoNombreCampo;
            await bot.SendMessage(chatId, GetEditModeInstructions(), cancellationToken: ct);
            return true;
        }

        return false;
    }

    public async Task<bool> HandleEditMode(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct, FlowLLMProcessor llmProcessor)
    {
        if (appt.CampoAEditar != Appointment.CampoPendiente.EsperandoNombreCampo) return false;

        // Intentar parsear campo + valor directo
        if (CamposExistentes.TryParseCambioCampo(rawText, out var campoCambio, out var valorDirecto))
        {
            if (!string.IsNullOrWhiteSpace(valorDirecto))
            {
                // NUEVO: Verificar si necesita normalización LLM
                if (campoCambio is Appointment.CampoPendiente.Lugar or 
                                  Appointment.CampoPendiente.Cirujano or 
                                  Appointment.CampoPendiente.Anestesiologo or
                                  Appointment.CampoPendiente.Cirugia)
                {
                    appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                    await bot.SendMessage(chatId, "⏳ Procesando...", cancellationToken: ct);
                    await llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                    return true;
                }
                else
                {
                    // Para campos que NO necesitan normalización
                    if (CamposExistentes.TryAplicarValorCampo(appt, campoCambio, valorDirecto, out var err))
                    {
                        appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                        await bot.SendMessage(chatId,
                            CamposExistentes.GenerarMensajeActualizacion(campoCambio, valorDirecto),
                            cancellationToken: ct);
                        
                        await TryConfirmation(bot, appt, chatId, ct);
                        return true;
                    }
                    else
                    {
                        await bot.SendMessage(chatId, err!, cancellationToken: ct);
                        return true;
                    }
                }
            }
            else
            {
                appt.CampoQueFalta = campoCambio;
                appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
                appt.IntentosCampoActual = 0;
                await bot.SendMessage(chatId,
                    $"Perfecto. Decime el nuevo valor para {CamposExistentes.NombreHumanoCampo(campoCambio)}:",
                    cancellationToken: ct);
                return true;
            }
        }
        
        // Intentar parsear solo el nombre del campo
        if (CamposExistentes.TryParseSoloCampo(rawText, out var soloNombreCampo))
        {
            appt.CampoQueFalta = soloNombreCampo;
            appt.CampoAEditar = Appointment.CampoPendiente.Ninguno;
            appt.IntentosCampoActual = 0;
            await bot.SendMessage(chatId,
                $"Perfecto. Decime el nuevo valor para {CamposExistentes.NombreHumanoCampo(soloNombreCampo)}:",
                cancellationToken: ct);
            return true;
        }

        await bot.SendMessage(chatId,
            "No entendí qué campo querés cambiar. Decí alguno de estos:\n" +
            "'fecha', 'hora', 'lugar', 'cirujano', 'cirugia', 'cantidad', 'anestesiologo'",
            cancellationToken: ct);
        return true;
    }

    public async Task<bool> HandleDirectChanges(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct, FlowLLMProcessor llmProcessor)
    {
        if (!CamposExistentes.TryParseCambioCampo(rawText, out var campoCambioDirecto, out var valorCambioDirecto) ||
            campoCambioDirecto == Appointment.CampoPendiente.Ninguno)
            return false;

        if (!string.IsNullOrWhiteSpace(valorCambioDirecto))
        {
            // Si es lugar, cirujano o anestesiologo, enviar al LLM para normalizar
            if (campoCambioDirecto is Appointment.CampoPendiente.Lugar or 
                                     Appointment.CampoPendiente.Cirujano or 
                                     Appointment.CampoPendiente.Anestesiologo or
                                     Appointment.CampoPendiente.Cirugia) // AGREGADO: cirugia también necesita normalización
            {
                await bot.SendMessage(chatId, "⏳ Procesando...", cancellationToken: ct);
                await llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                return true;
            }
            else
            {
                // CORREGIDO: Solo para campos que NO necesitan normalización (cantidad, fecha/hora)
                if (CamposExistentes.TryAplicarValorCampo(appt, campoCambioDirecto, valorCambioDirecto, out var err))
                {
                    await bot.SendMessage(chatId,
                        CamposExistentes.GenerarMensajeActualizacion(campoCambioDirecto, valorCambioDirecto),
                        cancellationToken: ct);
                    
                    await TryConfirmation(bot, appt, chatId, ct);
                    return true;
                }
                else
                {
                    await bot.SendMessage(chatId, err!, cancellationToken: ct);
                    return true;
                }
            }
        }

        // Si no hay valor, pedir el valor del campo
        appt.CampoQueFalta = campoCambioDirecto;
        appt.IntentosCampoActual = 0;
        await bot.SendMessage(chatId,
            $"Necesito el valor de {CamposExistentes.NombreHumanoCampo(campoCambioDirecto)}.",
            cancellationToken: ct);
        return true;
    }

    private string GetEditModeInstructions()
    {
        return "🔧 **¿Qué querés modificar?**\n\n" +
               "📝 Podés responder de dos formas:\n\n" +
               "**1️⃣ Solo el campo:**\n" +
               "• `fecha` • `hora` • `lugar`\n" +
               "• `cirujano` • `cirugia` • `cantidad` • `anestesiologo`\n\n" +
               "**2️⃣ Campo + nuevo valor:**\n" +
               "• `fecha 08/08` • `hora 15` • `lugar Italiano`\n" +
               "• `cirujano Rodriguez` • `cantidad 3`";
    }

    private async Task TryConfirmation(ITelegramBotClient bot, Appointment appt, long chatId, CancellationToken ct)
    {
        if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct))
            return;
        
        await bot.SendMessage(chatId,
            "Podés cambiar otro campo o escribir 'confirmar'.",
            cancellationToken: ct);
    }
}