using Telegram.Bot;
using Telegram.Bot.Exceptions;
using RegistroCx.Models;
using RegistroCx.Helpers;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services.Flow;

public class FlowWizardHandler
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;

    public async Task<bool> HandleFieldWizard(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        Console.WriteLine($"[WIZARD] CampoQueFalta={appt.CampoQueFalta}, ConfirmacionPendiente={appt.ConfirmacionPendiente}, rawText='{rawText}'");
        
        if (appt.CampoQueFalta == Appointment.CampoPendiente.Ninguno || appt.ConfirmacionPendiente)
        {
            Console.WriteLine("[WIZARD] Returning false - no field needed or confirmation pending");
            return false;
        }
        
        Console.WriteLine("[WIZARD] Processing field value...");
        var result = await ResolveFieldValue(bot, appt, rawText, chatId, ct);
        Console.WriteLine($"[WIZARD] ResolveFieldValue returned: {result}");
        return result;
    }

    private async Task<bool> ResolveFieldValue(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        var campoQueSeCompleto = appt.CampoQueFalta;
        
        // Intentar aplicar el valor
        if (!CamposExistentes.TryAplicarValorCampo(appt, appt.CampoQueFalta, rawText, out var error))
        {
            appt.IntentosCampoActual++;
            if (appt.IntentosCampoActual >= Appointment.MaxIntentosCampo)
            {
                appt.CampoQueFalta = Appointment.CampoPendiente.Ninguno;
                appt.IntentosCampoActual = 0;
                await SendMessageWithRetry(bot, chatId,
                    "No pude interpretar. Enviá todos los datos juntos (ej: 07/08 14hs Anchorena Fagoaga Rinoplastia x2 Jorge).",
                    ct);
                return true;
            }
            await SendMessageWithRetry(bot, chatId,
                $"{error} (Intento {appt.IntentosCampoActual}/{Appointment.MaxIntentosCampo}). Reintentá:",
                ct);
            return true;
        }
        
        // Limpiar el wizard
        appt.CampoQueFalta = Appointment.CampoPendiente.Ninguno;
        appt.IntentosCampoActual = 0;
        
        // Lógica inteligente según el tipo de campo
        return await HandleFieldCompletion(bot, appt, campoQueSeCompleto, rawText, chatId, ct);
    }

    private async Task<bool> HandleFieldCompletion(ITelegramBotClient bot, Appointment appt, 
        Appointment.CampoPendiente campoCompletado, string rawText, long chatId, CancellationToken ct)
    {
        if (NeedsLLMNormalization(campoCompletado))
        {
            // Campos que necesitan normalización por LLM - NO terminamos aquí, 
            // dejamos que continue al ProcessWithLLM en CirugiaFlowService
            return false;
        }
        else
        {
            // Campos que se pueden manejar directamente (fecha/hora, cantidad)
            await SendMessageWithRetry(bot, chatId,
                CamposExistentes.GenerarMensajeActualizacion(campoCompletado, rawText.Trim()),
                ct);
            
            // Intentar confirmar o pedir siguiente campo
            if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct)) 
                return true;
            
            if (await FlowValidationHelper.RequestMissingField(bot, appt, chatId, ct)) 
                return true;
            
            return false;
        }
    }

    public bool NeedsLLMNormalization(Appointment.CampoPendiente campo)
    {
        return campo is Appointment.CampoPendiente.Lugar or 
                       Appointment.CampoPendiente.Cirujano or 
                       Appointment.CampoPendiente.Anestesiologo or
                       Appointment.CampoPendiente.Cirugia;
    }

    private async Task SendMessageWithRetry(ITelegramBotClient bot, long chatId, string message, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await MessageSender.SendWithRetry(chatId, message, cancellationToken: ct);
                return; // Éxito, salir del loop
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429) // Too Many Requests
            {
                var delay = ex.Parameters?.RetryAfter ?? (BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == MaxRetries - 1)
                {
                    // Último intento fallido, log el error pero no lanzar excepción
                    Console.WriteLine($"Failed to send message after {MaxRetries} attempts: {ex.Message}");
                    return;
                }
                
                // Exponential backoff
                var delay = TimeSpan.FromMilliseconds(BaseDelayMs * Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation requested, don't retry
                throw;
            }
            catch (Exception ex)
            {
                // Otros errores no relacionados con red
                Console.WriteLine($"Unexpected error sending message: {ex.Message}");
                throw;
            }
        }
    }
}