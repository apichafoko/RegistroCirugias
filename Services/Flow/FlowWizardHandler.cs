using Telegram.Bot;
using Telegram.Bot.Exceptions;
using RegistroCx.Models;
using RegistroCx.Helpers;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services;
using RegistroCx.Services.Repositories;
using RegistroCx.Services.Analytics;
using RegistroCx.Services.UI;
using System.Linq;

namespace RegistroCx.Services.Flow;

public class FlowWizardHandler
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 1000;
    
    private readonly IAnesthesiologistSearchService? _anesthesiologistSearchService;
    private readonly IUserProfileRepository? _userRepository;
    private readonly IParsingAnalyticsService? _analytics;
    private readonly IQuickEditService? _quickEditService;
    
    // Constructor sin parámetros para compatibilidad
    public FlowWizardHandler() { }
    
    // Constructor con dependencias para nueva funcionalidad
    public FlowWizardHandler(IAnesthesiologistSearchService anesthesiologistSearchService, IUserProfileRepository userRepository, IParsingAnalyticsService? analytics = null, IQuickEditService? quickEditService = null)
    {
        _anesthesiologistSearchService = anesthesiologistSearchService;
        _userRepository = userRepository;
        _analytics = analytics;
        _quickEditService = quickEditService;
    }

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
        
        // VALIDACIÓN: Verificar si la respuesta tiene contexto apropiado para el campo solicitado
        if (!IsAppropriateResponseForField(appt.CampoQueFalta, rawText))
        {
            Console.WriteLine($"[WIZARD] Inappropriate response '{rawText}' for field {appt.CampoQueFalta}");
            
            appt.IntentosCampoActual++;
            if (appt.IntentosCampoActual >= Appointment.MaxIntentosCampo)
            {
                // NUEVO: En lugar de abandonar abruptamente, ofrecer opciones al usuario
                var helpMessage = GenerateContextualHelpMessage(campoQueSeCompleto, rawText);
                await SendMessageWithRetry(bot, chatId, helpMessage, ct);
                
                // No limpiar el campo todavía, dar una oportunidad más
                appt.IntentosCampoActual = Appointment.MaxIntentosCampo - 1; // Resetear para una oportunidad más
                return true;
            }
            
            // Dar feedback específico sobre lo que se espera
            var feedbackMessage = GenerateFieldFeedbackMessage(appt.CampoQueFalta, rawText);
            await SendMessageWithRetry(bot, chatId, 
                $"{feedbackMessage}\n\n(Intento {appt.IntentosCampoActual}/{Appointment.MaxIntentosCampo})", ct);
            return true;
        }
        
        // Lógica especial para búsqueda de anestesiólogos con LLM
        if (appt.CampoQueFalta == Appointment.CampoPendiente.Anestesiologo && 
            _anesthesiologistSearchService != null && 
            _userRepository != null)
        {
            var searchResult = await HandleAnesthesiologistSearch(bot, appt, rawText, chatId, ct);
            if (searchResult) return true;
        }
        
        // Intentar aplicar el valor
        if (!CamposExistentes.TryAplicarValorCampo(appt, appt.CampoQueFalta, rawText, out var error))
        {
            // Log failed field parsing
            if (_analytics != null)
            {
                var fieldName = appt.CampoQueFalta.ToString().ToLowerInvariant();
                await _analytics.TrackMissingFieldAsync(fieldName, rawText);
                
                if (appt.IntentosCampoActual == 0) // First attempt
                {
                    await _analytics.LogParsingErrorAsync($"invalid_field_{fieldName}", rawText, error);
                }
            }
            
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
        
        // Log successful field extraction
        if (_analytics != null)
        {
            var fieldName = appt.CampoQueFalta.ToString().ToLowerInvariant();
            await _analytics.TrackFieldExtractionAsync(fieldName, rawText, rawText);
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
            if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct, _quickEditService)) 
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

    /// <summary>
    /// Verifica si la respuesta del usuario es apropiada para el campo solicitado
    /// </summary>
    private static bool IsAppropriateResponseForField(Appointment.CampoPendiente campo, string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var normalizedResponse = response.Trim().ToLowerInvariant();

        // Palabras claramente no relacionadas con contexto médico
        var inappropriateWords = new[]
        {
            "perro", "gato", "auto", "casa", "verde", "azul", "rojo", "alto", "bajo",
            "edificio", "árbol", "mesa", "silla", "comida", "agua", "libro", "película",
            "música", "deporte", "fútbol", "parque", "playa", "dinero", "mercado"
        };

        // Si contiene palabras claramente inapropiadas, rechazar
        if (inappropriateWords.Any(word => normalizedResponse.Contains(word)))
        {
            return false;
        }

        return campo switch
        {
            Appointment.CampoPendiente.FechaHora => IsAppropriateDateTime(normalizedResponse),
            Appointment.CampoPendiente.Lugar => IsAppropriatePlace(normalizedResponse),
            Appointment.CampoPendiente.Cirujano => IsAppropriatePerson(normalizedResponse),
            Appointment.CampoPendiente.PreguntandoSiAsignarAnestesiologo => IsAppropriateYesNo(normalizedResponse),
            Appointment.CampoPendiente.SeleccionandoAnestesiologoCandidato => IsAppropriateNumber(normalizedResponse),
            Appointment.CampoPendiente.Anestesiologo => IsAppropriatePerson(normalizedResponse),
            Appointment.CampoPendiente.Cirugia => IsAppropriateSurgery(normalizedResponse),
            Appointment.CampoPendiente.Cantidad => IsAppropriateQuantity(normalizedResponse),
            _ => true // Por defecto, aceptar
        };
    }

    private static bool IsAppropriateDateTime(string response)
    {
        // Debe contener números, fechas o palabras temporales
        return response.Any(char.IsDigit) || 
               response.Contains("mañana") || response.Contains("hoy") || 
               response.Contains("ayer") || response.Contains("pasado") ||
               response.Contains("/") || response.Contains(":") || response.Contains("hs");
    }

    private static bool IsAppropriatePlace(string response)
    {
        // Debe ser texto de al menos 2 caracteres, no solo números
        return response.Length >= 2 && response.Any(char.IsLetter);
    }

    private static bool IsAppropriatePerson(string response)
    {
        // Aceptar respuestas que indican "ninguno/nadie"
        var emptyIndicators = new[] { "nadie", "ninguno", "ninguna", "no hay", "sin", "no", "n/a", "vacio", "vacío" };
        if (emptyIndicators.Any(indicator => response.Contains(indicator)))
            return true;
            
        // Debe ser texto de al menos 2 caracteres, principalmente letras
        return response.Length >= 2 && response.Any(char.IsLetter) && 
               response.Count(char.IsLetter) > response.Count(char.IsDigit);
    }

    private static bool IsAppropriateSurgery(string response)
    {
        // Debe ser texto, puede contener abreviaciones médicas
        return response.Length >= 2 && response.Any(char.IsLetter);
    }

    private static bool IsAppropriateQuantity(string response)
    {
        // Debe contener números o palabras de cantidad
        return response.Any(char.IsDigit) || 
               response.Contains("un") || response.Contains("dos") || 
               response.Contains("tres") || response.Contains("cuatro");
    }

    /// <summary>
    /// Genera mensaje de feedback específico para cada campo
    /// </summary>
    private static string GenerateFieldFeedbackMessage(Appointment.CampoPendiente campo, string userInput)
    {
        return campo switch
        {
            Appointment.CampoPendiente.FechaHora => 
                $"🤔 \"{userInput}\" no parece una fecha/hora.\n\n" +
                "💡 **Ejemplos válidos:** 16hs, 08/08, mañana, 15:30, 07/08 14hs",
            
            Appointment.CampoPendiente.Lugar => 
                $"🤔 \"{userInput}\" no parece el nombre de un hospital o clínica.\n\n" +
                "💡 **Ejemplos válidos:** Hospital Italiano, Sanatorio Anchorena, Clínica Santa Isabel",
            
            Appointment.CampoPendiente.Cirujano => 
                $"🤔 \"{userInput}\" no parece un nombre de cirujano.\n\n" +
                "💡 **Ejemplos válidos:** Dr. García, Dra. Rodríguez, García López, Dr. Martínez",
            
            Appointment.CampoPendiente.PreguntandoSiAsignarAnestesiologo =>
                $"🤔 \"{userInput}\" no es una respuesta clara.\n\n" +
                "💡 **Respondé simplemente:** 'sí', 'si', 'no' o 'n'",
                
            Appointment.CampoPendiente.SeleccionandoAnestesiologoCandidato =>
                $"🤔 \"{userInput}\" no parece un número de la lista.\n\n" +
                "💡 **Respondé con el número:** 1, 2, 3, etc.",
                
            Appointment.CampoPendiente.Anestesiologo => 
                $"🤔 \"{userInput}\" no parece un nombre de anestesiólogo.\n\n" +
                "💡 **Ejemplos válidos:** Dr. Pérez, Dra. González, Martínez, Dr. López",
            
            Appointment.CampoPendiente.Cirugia => 
                $"🤔 \"{userInput}\" no parece un tipo de cirugía.\n\n" +
                "💡 **Ejemplos válidos:** CERS, MLD, apendicectomía, cesárea, adenoides, amígdalas",
            
            Appointment.CampoPendiente.Cantidad => 
                $"🤔 \"{userInput}\" no parece una cantidad válida.\n\n" +
                "💡 **Ejemplos válidos:** 1, 2, 3, dos, tres, una",
            
            _ => $"No entiendo \"{userInput}\" para este campo. Por favor, intentá con otro valor."
        };
    }

    private static bool IsAppropriateYesNo(string response)
    {
        // Verifica si la respuesta es una respuesta válida de sí/no
        return response.Contains("sí") || response.Contains("si") || response.Contains("yes") || response.Contains("y") ||
               response.Contains("no") || response.Contains("n");
    }
    
    private static bool IsAppropriateNumber(string response)
    {
        // Verifica si la respuesta contiene un número
        return response.Any(char.IsDigit);
    }

    /// <summary>
    /// Genera mensaje de ayuda específico cuando se alcanzan los máximos intentos
    /// </summary>
    private static string GenerateFieldSpecificHelpMessage(Appointment.CampoPendiente campo)
    {
        var fieldName = CamposExistentes.NombreHumanoCampo(campo);
        
        return $"❌ **No pude interpretar {fieldName} después de varios intentos.**\n\n" +
               "🔄 **Recomendación:** Enviá todos los datos de tu cirugía en un solo mensaje.\n\n" +
               "✨ **Ejemplo completo:** `2 CERS mañana 14hs Hospital Italiano Dr. García López Dr. Martínez`\n\n" +
               "💡 Esto es más rápido y evita confusiones. ¡Probá ahora!";
    }

    /// <summary>
    /// Genera mensaje contextual cuando el usuario parece desviarse del tema
    /// </summary>
    private static string GenerateContextualHelpMessage(Appointment.CampoPendiente campo, string userInput)
    {
        var fieldName = CamposExistentes.NombreHumanoCampo(campo);
        
        return $"Me pusiste \"{userInput}\" pero necesito **{fieldName}** para seguir.\n\n" +
               "Podés:\n" +
               "• Probar con **{fieldName}** otra vez\n" +
               "• Poner **\"nuevo\"** si querés arrancar de vuelta\n" +
               "• O mandarme todo: `mañana 14hs Hospital Dr. García CERS`\n\n" +
               "Dale, tranqui! 😊";
    }

    /// <summary>
    /// Maneja la búsqueda de anestesiólogos con LLM
    /// </summary>
    private async Task<bool> HandleAnesthesiologistSearch(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        try
        {
            // Obtener el email del equipo del usuario
            var userProfile = await _userRepository!.GetAsync(chatId, ct);
            if (userProfile?.GoogleEmail == null)
            {
                await SendMessageWithRetry(bot, chatId, "No pude encontrar tu email para buscar anestesiólogos del equipo.", ct);
                return false;
            }

            // Buscar candidatos con LLM
            var candidates = await _anesthesiologistSearchService!.SearchByPartialNameAsync(rawText, userProfile.GoogleEmail);
            
            if (candidates.Count == 0)
            {
                // No se encontraron candidatos, usar el valor tal como está
                appt.Anestesiologo = Capitalizador.CapitalizarSimple(rawText);
                await SendMessageWithRetry(bot, chatId, 
                    $"✅ No encontré coincidencias exactas en el equipo, pero agregué '{appt.Anestesiologo}' como anestesiólogo.", ct);
                
                // Marcar campo como completado
                appt.CampoQueFalta = Appointment.CampoPendiente.Ninguno;
                appt.IntentosCampoActual = 0;
                return true;
            }
            
            if (candidates.Count == 1)
            {
                // Un solo candidato, asignar directamente
                appt.Anestesiologo = candidates[0].Nombre;
                await SendMessageWithRetry(bot, chatId, 
                    $"✅ Perfecto! Encontré y asigné: **{candidates[0].Nombre}**", ct);
                
                // Marcar campo como completado
                appt.CampoQueFalta = Appointment.CampoPendiente.Ninguno;
                appt.IntentosCampoActual = 0;
                return true;
            }
            
            // Múltiples candidatos, mostrar opciones
            var candidateNames = candidates.Select(c => c.Nombre).ToList();
            appt.AnesthesiologistCandidates = candidateNames;
            appt.CampoQueFalta = Appointment.CampoPendiente.SeleccionandoAnestesiologoCandidato;
            appt.IntentosCampoActual = 0;
            
            var options = string.Join("\n", candidateNames.Select((name, index) => $"{index + 1}. {name}"));
            await SendMessageWithRetry(bot, chatId,
                $"🔍 Encontré varias opciones. Seleccioná el número:\n\n{options}\n\nResponde con el número (1, 2, 3, etc.)", ct);
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WIZARD] Error in anesthesiologist search: {ex.Message}");
            // Fallback al comportamiento original
            return false;
        }
    }
}