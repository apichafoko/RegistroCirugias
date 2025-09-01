using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Services.Extraction;
using RegistroCx.Services.Flow;
using RegistroCx.Services.Reports;
using RegistroCx.Helpers._0Auth;
using RegistroCx.Services.Repositories;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services.Analytics;
using RegistroCx.Services.Caching;
using RegistroCx.Services.UI;
using RegistroCx.Helpers;
using RegistroCx.Services.Context;
using RegistroCx.models;
using System.Text.RegularExpressions;

namespace RegistroCx.Services;

/// <summary>
/// Servicio principal para gestionar el flujo conversacional de registro de cirugías vía Telegram.
/// 
/// <para>📁 Estructura de archivos creada:</para>
/// <list type="bullet">
///   <item><description><b>CirugiaFlowService.cs</b> - Archivo principal (orquestador)</description></item>
///   <item><description><b>FlowStateManager.cs</b> - Gestión de estado y contexto</description></item>
///   <item><description><b>FlowMessageHandler.cs</b> - Manejo de mensajes y respuestas</description></item>
///   <item><description><b>FlowValidationHelper.cs</b> - Validaciones y confirmaciones</description></item>
///   <item><description><b>FlowWizardHandler.cs</b> - Wizard de campos paso a paso</description></item>
///   <item><description><b>FlowLLMProcessor.cs</b> - Procesamiento con LLM</description></item>
///   <item><description><b>LLMContextManager.cs</b> (ya creado antes) - Contexto inteligente para LLM</description></item>
/// </list>
/// 
/// <para>🎯 <b>Ventajas de esta refactorización:</b></para>
/// <list type="bullet">
///   <item><description><b>📦 Separación de responsabilidades:</b></description></item>
///   <item><description>• <b>Estado:</b> FlowStateManager maneja conversaciones activas</description></item>
///   <item><description>• <b>Mensajes:</b> FlowMessageHandler procesa respuestas del usuario</description></item>
///   <item><description>• <b>Validación:</b> FlowValidationHelper confirma y valida datos</description></item>
///   <item><description>• <b>Wizard:</b> FlowWizardHandler guía paso a paso</description></item>
///   <item><description>• <b>LLM:</b> FlowLLMProcessor normaliza con inteligencia artificial</description></item>
/// </list>
/// 
/// <para>🧹 <b>Código más limpio:</b></para>
/// <list type="bullet">
///   <item><description>• <b>Archivos pequeños:</b> Cada uno ~100-200 líneas máximo</description></item>
///   <item><description>• <b>Fácil mantenimiento:</b> Cambios aislados por funcionalidad</description></item>
///   <item><description>• <b>Fácil testing:</b> Cada componente se puede probar independientemente</description></item>
///   <item><description>• <b>Fácil extensión:</b> Agregar nuevas funcionalidades sin tocar el core</description></item>
/// </list>
/// </summary>
/// 📁 Estructura de archivos creada:


public class CirugiaFlowService
{
    private readonly LLMOpenAIAssistant _llm;
    private readonly Dictionary<long, Appointment> _pending;

    // Helpers especializados
    private readonly FlowStateManager _stateManager;
    private readonly FlowMessageHandler _messageHandler;
    private readonly FlowWizardHandler _wizardHandler;
    private readonly FlowLLMProcessor _llmProcessor;
    private readonly AppointmentConfirmationService _confirmationService;
    private readonly MultiSurgeryParser _multiSurgeryParser;
    private readonly IUserProfileRepository _userRepo;
    private readonly UserLearningService _learningService;
    
    // Nuevos servicios para modificación
    private readonly AppointmentSearchService _searchService;
    private readonly AppointmentModificationService _modificationService;
    private readonly AppointmentUpdateCoordinator _updateCoordinator;
    
    // Servicios de MVP improvements
    private readonly IParsingAnalyticsService _analytics;
    private readonly ICacheService _cache;
    private readonly IQuickEditService _quickEdit;
    private readonly IConversationContextManager _contextManager;
    
    // Sistema de equipos
    private readonly EquipoService _equipoService;
    
    // Validación de contexto médico
    private readonly MedicalContextValidator _medicalValidator;

    public CirugiaFlowService(
        LLMOpenAIAssistant llm, 
        Dictionary<long, Appointment> pending,
        AppointmentConfirmationService confirmationService,
        IGoogleOAuthService oauthService,
        IUserProfileRepository userRepo,
        CalendarSyncService calendarSync,
        IAppointmentRepository appointmentRepo,
        MultiSurgeryParser multiSurgeryParser,
        IReportService reportService,
        IAnesthesiologistSearchService anesthesiologistSearchService,
        UserLearningService learningService,
        AppointmentSearchService searchService,
        AppointmentModificationService modificationService,
        AppointmentUpdateCoordinator updateCoordinator,
        IParsingAnalyticsService analytics,
        ICacheService cache,
        IQuickEditService quickEdit,
        IConversationContextManager contextManager,
        EquipoService equipoService,
        MedicalContextValidator medicalValidator)
    {
        _llm = llm;
        _pending = pending;
        _confirmationService = confirmationService;
        _multiSurgeryParser = multiSurgeryParser;
        _userRepo = userRepo;
        _learningService = learningService;
        _searchService = searchService;
        _modificationService = modificationService;
        _updateCoordinator = updateCoordinator;
        _analytics = analytics;
        _cache = cache;
        _quickEdit = quickEdit;
        _contextManager = contextManager;
        _equipoService = equipoService;
        _medicalValidator = medicalValidator;
        _stateManager = new FlowStateManager(_pending);
        _messageHandler = new FlowMessageHandler(oauthService, userRepo, calendarSync, appointmentRepo, reportService, quickEdit);
        _wizardHandler = new FlowWizardHandler(anesthesiologistSearchService, userRepo, analytics, quickEdit);
        _llmProcessor = new FlowLLMProcessor(llm, quickEdit);
    }

    public async Task HandleAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        // Manejar comandos especiales primero (sin enviar "Procesando..." ya que el otro servicio lo hará)
        if (await _messageHandler.HandleSpecialCommandsAsync(bot, chatId, rawText, ct))
        {
            _stateManager.ClearContext(chatId);
            return;
        }
        
        // CRÍTICO: Verificar comando "cancelar" ANTES de todo
        if (IsCancelCommand(rawText))
        {
            Console.WriteLine("[FLOW] 🚫 Cancel command detected, clearing context");
            _stateManager.ClearContext(chatId);
            await MessageSender.SendWithRetry(chatId, "❌ Operación cancelada. Podés empezar de nuevo enviando los datos de tu cirugía.", cancellationToken: ct);
            return;
        }

        // PRIORIDAD: Verificar si QuickEditService puede manejar el texto (estados de edición)
        if (await _quickEdit.TryHandleTextInputAsync(bot, chatId, rawText, ct))
        {
            // Texto manejado por estado de edición, no continuar
            return;
        }

        // INMEDIATO: Enviar mensaje de "Procesando..." para reducir ansiedad del usuario
        await MessageSender.SendWithRetry(chatId, "⏳ Procesando...", cancellationToken: ct);

        // NUEVA LÓGICA: Verificar contexto conversacional antes de clasificar intent
        var appt = _stateManager.GetOrCreateAppointment(chatId);
        var currentContext = _contextManager.ExtractContext(appt);
        
        // Si hay contexto activo, verificar relevancia del mensaje
        if (currentContext.Type != ContextType.None)
        {
            var relevance = await _contextManager.AnalyzeMessageRelevanceAsync(rawText, currentContext, ct);
            
            if (!relevance.IsRelevant)
            {
                // Manejar desviación de contexto
                if (await _contextManager.HandleContextDeviationAsync(bot, chatId, rawText, currentContext, ct))
                {
                    // Esperar respuesta del usuario sobre si quiere continuar o cambiar
                    return;
                }
            }
            
            // Si debe saltear intent classification, ir directo al wizard/confirmación
            if (_contextManager.ShouldBypassIntentClassification(rawText, currentContext))
            {
                await HandleWithActiveContext(bot, chatId, rawText, appt, currentContext, ct);
                return;
            }
        }

        // 1. CLASIFICAR INTENT del mensaje (solo si no hay contexto activo o es cambio explícito)
        var intent = await _llmProcessor.ClassifyIntentAsync(rawText);
        
        // 2. Manejar intents de modificación
        if (intent == MessageIntent.ModifySurgery)
        {
            await HandleModificationAsync(bot, chatId, rawText, ct);
            return;
        }
        
        // 3. Manejar intents de cancelación
        if (intent == MessageIntent.CancelSurgery)
        {
            await HandleCancellationAsync(bot, chatId, rawText, ct);
            return;
        }
        
        // 4. Manejar intents de consulta
        if (intent == MessageIntent.QuerySurgery)
        {
            await HandleQueryAsync(bot, chatId, rawText, ct);
            return;
        }

        // El appointment ya fue obtenido arriba para análisis de contexto
        appt.HistoricoInputs.Add(rawText);

        // Manejar continuación después de warning de validación
        if (!string.IsNullOrEmpty(appt.ValidationWarning) && 
            rawText.Trim().ToLowerInvariant() is "continuar" or "ok" or "continúar" or "si" or "sí")
        {
            Console.WriteLine("[FLOW] User confirmed to continue after validation warning");
            // Limpiar el warning y continuar con el flujo normal
            appt.ValidationWarning = null;
            // El input original está en HistoricoInputs[^2] (penúltimo)
            if (appt.HistoricoInputs.Count >= 2)
            {
                var originalInput = appt.HistoricoInputs[^2];
                Console.WriteLine($"[FLOW] Continuing with original input: {originalInput}");
                // Procesar el input original sin validaciones
                await _llmProcessor.ProcessWithLLM(bot, appt, originalInput, chatId, ct);
                return;
            }
        }

        // Manejar captura de email del anestesiólogo
        if (await HandleEmailCapture(bot, appt, rawText, chatId, ct))
        {
            // Si el appointment está listo para limpieza, limpiar el contexto
            if (appt.ReadyForCleanup)
            {
                Console.WriteLine("[FLOW] Appointment ready for cleanup after email handling - clearing context");
                _stateManager.ClearContext(chatId);
            }
            return;
        }

        // Manejar confirmación
        if (await HandleConfirmationFlow(bot, appt, rawText, chatId, ct))
        {
            return;
        }

        // Manejar modo edición
        if (await _messageHandler.HandleEditMode(bot, appt, rawText, chatId, ct, _llmProcessor))
        {
            return;
        }

        Console.WriteLine($"[FLOW] Before wizard - CampoQueFalta={appt.CampoQueFalta}, ConfirmacionPendiente={appt.ConfirmacionPendiente}");
        
        // Manejar wizard de campos
        if (await _wizardHandler.HandleFieldWizard(bot, appt, rawText, chatId, ct))
        {
            Console.WriteLine("[FLOW] Wizard handled the message - returning");
            return;
        }
        
        Console.WriteLine("[FLOW] Wizard did not handle - checking direct changes");

        // Manejar cambios directos
        if (await _messageHandler.HandleDirectChanges(bot, appt, rawText, chatId, ct, _llmProcessor))
        {
            Console.WriteLine("[FLOW] Direct changes handled - returning");
            return;
        }

        Console.WriteLine("[FLOW] Going to LLM - no other handler processed the message");
        
        // VALIDACIÓN: Verificar si el texto tiene contexto médico relevante
        if (!await _medicalValidator.HasMedicalContextAsync(rawText, ct))
        {
            Console.WriteLine($"[FLOW] ❌ Non-medical context detected: {rawText}");
            
            // Si es texto claramente inconexo (como "perro verde"), dar mensaje específico
            if (_medicalValidator.IsDisconnectedWords(rawText))
            {
                var specificMessage = _medicalValidator.GenerateNonMedicalMessage(rawText);
                await MessageSender.SendWithRetry(chatId, specificMessage, cancellationToken: ct);
            }
            else
            {
                // Para otros casos, mostrar ayuda general
                var helpMessage = _medicalValidator.GenerateHelpMessage();
                await MessageSender.SendWithRetry(chatId, helpMessage, cancellationToken: ct);
            }
            
            // Limpiar contexto para empezar de nuevo
            _stateManager.ClearContext(chatId);
            return;
        }
        
        // CRÍTICO: Verificar intenciones de modificación ANTES de asumir que es nuevo registro
        // Esto previene que "quiero cambiar..." sea procesado como nueva cirugía
        if (IsModificationIntent(rawText))
        {
            Console.WriteLine("[FLOW] 🔧 Modification intent detected, routing to HandleModificationAsync");
            await HandleModificationAsync(bot, chatId, rawText, ct);
            return;
        }

        // NUEVO: Detectar múltiples cirugías ANTES del procesamiento LLM con validaciones completas
        if (appt.HistoricoInputs.Count == 1) // Solo para el primer input del usuario
        {
            Console.WriteLine("[FLOW] First user input - checking for multiple surgeries with validation");
            
            // Obtener perfil del usuario para acceso a listas de referencia
            var profile = await _userRepo.GetAsync(chatId, ct);
            // TODO: Implementar GetListasReferencia() en UserProfile cuando sea necesario
            var listasObj = (object?)null; // Por ahora null, el sistema funcionará sin listas específicas
            var referenceDate = DateTime.Now;
            
            var parseResult = await _multiSurgeryParser.ParseInputAsync(rawText, referenceDate, listasObj, chatId);
            
            // Manejar problemas de validación según severidad
            if (parseResult.ValidationStatus == "error" || 
                (parseResult.ValidationStatus == "warning" && parseResult.NeedsClarification))
            {
                Console.WriteLine($"[FLOW] ⚠️ Validation issues found: {parseResult.ValidationStatus}");
                
                // Enviar respuesta de validación al usuario
                var responseMessage = parseResult.SuggestedResponse ?? "No entiendo ese tipo de mensaje. ¿Podrías ser más específico?";
                
                // Para warnings, agregar opción de continuar
                if (parseResult.ValidationStatus == "warning")
                {
                    responseMessage += "\n\n💡 Si querés continuar de todas formas, escribí 'continuar' o 'ok'.";
                }
                
                // Agregar información de problemas específicos si los hay
                if (parseResult.Issues.Any())
                {
                    var issueMessages = string.Join("\n", parseResult.Issues.Select(i => $"• {i.Message}"));
                    responseMessage += $"\n\n{issueMessages}";
                }
                
                await MessageSender.SendWithRetry(chatId, responseMessage, cancellationToken: ct);
                
                // Para errors: limpiar contexto inmediatamente
                // Para warnings: guardar estado para permitir continuar si el usuario confirma
                if (parseResult.ValidationStatus == "error")
                {
                    _stateManager.ClearContext(chatId);
                }
                else
                {
                    // Guardar el parseResult en el appointment para manejarlo en la próxima iteración
                    appt.ValidationWarning = parseResult.SuggestedResponse;
                }
                
                return;
            }
            
            if (parseResult.IsMultiple)
            {
                Console.WriteLine($"[FLOW] ✅ Multiple surgeries detected at start: {parseResult.IndividualInputs.Count} surgeries");
                
                // GUARDAR la información de múltiples cirugías en el appointment
                appt.Notas = $"MULTIPLE_DETECTED:{parseResult.IndividualInputs.Count}|" + 
                            string.Join("|", parseResult.DetectedSurgeries.Select(s => $"{s.Quantity}:{s.SurgeryName}"));
                
                Console.WriteLine($"[FLOW] Saved multiple surgery info in Notas: {appt.Notas}");
                
                // Continúar con flujo normal para completar datos faltantes
                // La detección se aplicará cuando todo esté completo
            }
            else
            {
                Console.WriteLine("[FLOW] Single surgery detected - proceeding normally");
            }
        }
        
        // Procesar con LLM para casos nuevos (primero completar todos los datos)
        await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
    }

    private async Task<bool> HandleConfirmationFlow(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        var inputLower = rawText.Trim().ToLowerInvariant();
        
        // NUEVO: Verificar si es una confirmación de modificación
        if (appt.ModificationContext?.IsAwaitingConfirmation == true)
        {
            if (inputLower is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar")
            {
                // Ejecutar la modificación usando el appointment modificado completo
                if (appt.ModificationContext.ModifiedAppointment != null)
                {
                    var success = await _updateCoordinator.ExecuteDirectModificationAsync(
                        appt.ModificationContext.OriginalAppointment!, 
                        appt.ModificationContext.ModifiedAppointment!, 
                        chatId, 
                        ct);
                }
                else
                {
                    // Fallback al método anterior si no hay ModifiedAppointment
                    var success = await _updateCoordinator.ExecuteModificationAsync(
                        appt.ModificationContext.OriginalAppointment!, 
                        appt.ModificationContext.RequestedChanges!, 
                        chatId, 
                        ct);
                }
                
                // Limpiar contexto
                appt.ModificationContext = null;
                _stateManager.ClearContext(chatId);
                
                return true;
            }
            else if (inputLower.StartsWith("no"))
            {
                await MessageSender.SendWithRetry(chatId,
                    "❌ Modificación cancelada. Los datos originales se mantienen sin cambios.",
                    cancellationToken: ct);
                
                appt.ModificationContext = null;
                _stateManager.ClearContext(chatId);
                
                return true;
            }
            
            // Si no es ni sí ni no, pedir confirmación clara
            await MessageSender.SendWithRetry(chatId,
                "❓ Por favor confirma con 'sí' o 'no' si querés realizar estos cambios.",
                cancellationToken: ct);
            
            return true;
        }
        
        // Verificar si es una confirmación de múltiples cirugías
        if (appt.ConfirmacionPendiente && !string.IsNullOrWhiteSpace(appt.Notas) && 
            appt.Notas.StartsWith("MARKER_MULTIPLE_SURGERIES:"))
        {
            if (inputLower is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar" or "confirmar todas")
            {
                await HandleMultipleConfirmation(bot, appt, chatId, ct);
                return true;
            }
            else if (inputLower.StartsWith("no") || IsMultipleSurgeryEditCommand(rawText))
            {
                await HandleMultipleSurgeryEdit(bot, appt, rawText, chatId, ct);
                return true;
            }
        }
        
        // Primero dejar que el handler maneje la lógica básica de confirmación
        if (await _messageHandler.HandleConfirmationAsync(bot, appt, rawText, chatId, ct))
        {
            // Si era una confirmación positiva y ya no está pendiente, verificar si hay múltiples cirugías
            if (appt.ConfirmacionPendiente == false && inputLower is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar")
            {
                // NUEVO: Verificar si habíamos detectado múltiples cirugías anteriormente
                if (!string.IsNullOrEmpty(appt.Notas) && appt.Notas.StartsWith("MULTIPLE_DETECTED:"))
                {
                    Console.WriteLine($"[FLOW] Found saved multiple surgery info: {appt.Notas}");
                    
                    // Parsear la información guardada
                    var parseResult = ParseSavedMultipleSurgeryInfo(appt.Notas, appt.HistoricoInputs[0]);
                    
                    if (parseResult.IsMultiple)
                    {
                        Console.WriteLine($"[FLOW] Processing saved multiple surgeries after validation: {parseResult.IndividualInputs.Count} surgeries");
                        
                        // Limpiar el contexto actual antes de procesar múltiples
                        _stateManager.ClearContext(chatId);
                        
                        // Procesar múltiples cirugías con todos los datos ya validados
                        await HandleMultipleSurgeriesAfterValidation(bot, parseResult, appt, chatId, ct);
                    }
                    else
                    {
                        // Fallback a procesamiento normal
                        await _confirmationService.ProcessConfirmationAsync(bot, appt, chatId, ct);
                        
                        // Solo limpiar contexto si no está esperando email del anestesiólogo
                        if (appt.CampoQueFalta != Appointment.CampoPendiente.EsperandoEmailAnestesiologo)
                        {
                            _stateManager.ClearContext(chatId);
                        }
                    }
                }
                else
                {
                    // Procesar confirmación normal (una sola cirugía)
                    await _confirmationService.ProcessConfirmationAsync(bot, appt, chatId, ct);
                    
                    // Solo limpiar contexto si no está esperando email del anestesiólogo
                    if (appt.CampoQueFalta != Appointment.CampoPendiente.EsperandoEmailAnestesiologo)
                    {
                        _stateManager.ClearContext(chatId);
                    }
                }
            }
            return true;
        }
        return false;
    }

    private async Task HandleMultipleSurgeriesFromStart(ITelegramBotClient bot, MultiSurgeryParser.ParseResult parseResult, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[FLOW] Starting multiple surgery processing from the beginning with {parseResult.IndividualInputs.Count} surgeries");
            
            await MessageSender.SendWithRetry(chatId, 
                $"🔍 **Detecté {parseResult.IndividualInputs.Count} cirugías diferentes:**\n" +
                string.Join("\n", parseResult.DetectedSurgeries.Select((s, i) => $"{i + 1}. {s.Quantity} {s.SurgeryName}")) + 
                "\n\n⚡ Procesando cada cirugía por separado...", 
                cancellationToken: ct);

            var appointments = new List<Appointment>();
            
            // Procesar cada cirugía individualmente
            for (int i = 0; i < parseResult.IndividualInputs.Count; i++)
            {
                var individualInput = parseResult.IndividualInputs[i];
                var surgeryInfo = parseResult.DetectedSurgeries[i];
                
                Console.WriteLine($"[FLOW] Processing surgery {i + 1}: {individualInput}");
                
                // Crear un chatId temporal único para cada cirugía
                var tempChatId = chatId + (i + 1) * 10000; // Offset para evitar conflictos
                var tempAppt = _stateManager.GetOrCreateAppointment(tempChatId);
                tempAppt.HistoricoInputs.Add(individualInput);
                
                // Procesar con LLM individual
                await _llmProcessor.ProcessWithLLM(bot, tempAppt, individualInput, tempChatId, ct);
                
                appointments.Add(tempAppt);
            }
            
            // Crear marcador para el flujo de confirmación múltiple
            var markerAppt = _stateManager.GetOrCreateAppointment(chatId);
            markerAppt.ConfirmacionPendiente = true;
            markerAppt.Notas = $"MULTIPLE_SURGERIES:{parseResult.IndividualInputs.Count}";
            
            Console.WriteLine($"[FLOW] Multiple surgery processing initiated for {parseResult.IndividualInputs.Count} surgeries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FLOW] Error in HandleMultipleSurgeriesFromStart: {ex}");
            await MessageSender.SendWithRetry(chatId, 
                "❌ Error procesando múltiples cirugías. Intenta nuevamente.", 
                cancellationToken: ct);
        }
    }
    
    private MultiSurgeryParser.ParseResult ParseSavedMultipleSurgeryInfo(string notas, string originalInput)
    {
        try
        {
            // Formato: "MULTIPLE_DETECTED:2|1:HAVA|2:CERS"
            var parts = notas.Split('|');
            if (parts.Length < 2 || !parts[0].StartsWith("MULTIPLE_DETECTED:"))
            {
                return new MultiSurgeryParser.ParseResult { IsMultiple = false, IndividualInputs = new List<string> { originalInput } };
            }
            
            var countStr = parts[0].Substring("MULTIPLE_DETECTED:".Length);
            if (!int.TryParse(countStr, out var count))
            {
                return new MultiSurgeryParser.ParseResult { IsMultiple = false, IndividualInputs = new List<string> { originalInput } };
            }
            
            var surgeries = new List<MultiSurgeryParser.SurgeryInfo>();
            var individualInputs = new List<string>();
            
            // Extraer contexto base del input original (sin las cirugías)
            var baseContext = ExtractBaseContextFromOriginalInput(originalInput);
            
            for (int i = 1; i < parts.Length && i <= count; i++)
            {
                var surgeryPart = parts[i];
                var surgeryData = surgeryPart.Split(':');
                if (surgeryData.Length == 2 && int.TryParse(surgeryData[0], out var quantity))
                {
                    var surgeryName = surgeryData[1];
                    surgeries.Add(new MultiSurgeryParser.SurgeryInfo
                    {
                        Quantity = quantity,
                        SurgeryName = surgeryName,
                        OriginalText = $"{quantity} {surgeryName}"
                    });
                    
                    // Crear input individual con contexto base
                    individualInputs.Add($"{quantity} {surgeryName} {baseContext}".Trim());
                }
            }
            
            Console.WriteLine($"[FLOW] Parsed {surgeries.Count} surgeries from saved info");
            
            return new MultiSurgeryParser.ParseResult
            {
                IsMultiple = surgeries.Count > 1,
                OriginalInput = originalInput,
                IndividualInputs = individualInputs,
                DetectedSurgeries = surgeries
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FLOW] Error parsing saved multiple surgery info: {ex}");
            return new MultiSurgeryParser.ParseResult { IsMultiple = false, IndividualInputs = new List<string> { originalInput } };
        }
    }
    
    private string ExtractBaseContextFromOriginalInput(string originalInput)
    {
        // Remover patrones conocidos de múltiples cirugías del input original
        var patterns = new[]
        {
            @"\d+\s+[A-Za-zÁÉÍÓÚáéíóúñÑ]+\s+y\s+\d+\s+[A-Za-zÁÉÍÓÚáéíóúñÑ]+",
            @"\d+\s+[A-Za-zÁÉÍÓÚáéíóúñÑ]+\s*\+\s*\d+\s+[A-Za-zÁÉÍÓÚáéíóúñÑ]+",
            @"[A-Za-zÁÉÍÓÚáéíóúñÑ]+\s*x\s*\d+\s*,\s*[A-Za-zÁÉÍÓÚáéíóúñÑ]+\s*x\s*\d+",
            @"\d+\s*x\s*[A-Za-zÁÉÍÓÚáéíóúñÑ]+\s*,\s*\d+\s*x\s*[A-Za-zÁÉÍÓÚáéíóúñÑ]+"
        };
        
        var baseContext = originalInput;
        
        foreach (var pattern in patterns)
        {
            baseContext = System.Text.RegularExpressions.Regex.Replace(baseContext, pattern, "", RegexOptions.IgnoreCase);
        }
        
        // Limpiar espacios múltiples
        baseContext = System.Text.RegularExpressions.Regex.Replace(baseContext, @"\s+", " ").Trim();
        
        Console.WriteLine($"[FLOW] Extracted base context: '{baseContext}' from '{originalInput}'");
        return baseContext;
    }

    private async Task HandleMultipleConfirmation(ITelegramBotClient bot, Appointment markerAppt, long chatId, CancellationToken ct)
    {
        try
        {
            // Extraer número de cirugías del marker
            var parts = markerAppt.Notas?.Split(':');
            if (parts == null || parts.Length < 2)
            {
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando confirmación múltiple - formato inválido.", cancellationToken: ct);
                return;
            }
            var countStr = parts[1];
            if (!int.TryParse(countStr, out var surgeryCount))
            {
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando confirmación múltiple.", cancellationToken: ct);
                return;
            }

            var successCount = 0;
            var errors = new List<string>();
            var confirmedSurgeries = new List<string>();
            var emailRequests = new List<string>();

            // Procesar cada cirugía guardada SILENCIOSAMENTE
            for (int i = 1; i <= surgeryCount; i++)
            {
                try
                {
                    var tempChatId = chatId + i * 100;
                    var savedAppt = _stateManager.GetOrCreateAppointment(tempChatId);
                    
                    if (!string.IsNullOrWhiteSpace(savedAppt.Cirugia))
                    {
                        // Procesar confirmación sin enviar mensajes individuales (modo silencioso)
                        var success = await _confirmationService.ProcessConfirmationAsync(bot, savedAppt, chatId, ct, silent: true);
                        
                        if (success)
                        {
                            successCount++;
                            confirmedSurgeries.Add($"✅ **{savedAppt.Cantidad} {savedAppt.Cirugia?.ToUpper()}**");
                            
                            // Si hay anestesiólogo sin email, agregar a lista de pendientes
                            if (!string.IsNullOrWhiteSpace(savedAppt.Anestesiologo) && 
                                savedAppt.CampoQueFalta == Appointment.CampoPendiente.EsperandoEmailAnestesiologo)
                            {
                                emailRequests.Add($"• {savedAppt.Anestesiologo}");
                            }
                        }
                        else
                        {
                            errors.Add($"Cirugía {i}: {savedAppt.Cirugia}");
                        }
                    }
                    
                    // Limpiar contexto temporal
                    _stateManager.ClearContext(tempChatId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MULTI-CONFIRM] Error confirming surgery {i}: {ex}");
                    errors.Add($"Cirugía {i}");
                }
            }

            // MENSAJE CONSOLIDADO ÚNICO
            var finalMessage = $"🎉 **¡{successCount} cirugías confirmadas exitosamente!**\n\n";
            
            // Lista de cirugías confirmadas
            finalMessage += string.Join("\n", confirmedSurgeries);
            
            // Información adicional
            finalMessage += "\n\n📅 **Eventos creados en Google Calendar con recordatorio de 24hs**";
            finalMessage += "\n💾 **Guardadas en la base de datos**";
            
            // Si hay emails pendientes
            if (emailRequests.Count > 0)
            {
                finalMessage += "\n\n📧 **Emails pendientes para anestesiólogos:**\n";
                finalMessage += string.Join("\n", emailRequests);
                finalMessage += "\n\n💡 Podés enviarme los emails o escribir 'saltar' para omitir.";
            }
            
            // Si hubo errores
            if (errors.Count > 0)
            {
                finalMessage += $"\n\n❌ **{errors.Count} cirugías fallaron:**\n{string.Join("\n", errors)}";
            }

            await MessageSender.SendWithRetry(chatId, finalMessage, cancellationToken: ct);
            
            // Limpiar contexto principal
            _stateManager.ClearContext(chatId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-CONFIRM] Error in multiple confirmation: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error procesando confirmación múltiple. Intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private bool IsMultipleSurgeryEditCommand(string input)
    {
        var inputLower = input.ToLowerInvariant();
        
        // Patrones de edición granular
        var editPatterns = new[]
        {
            @"cirug[íi]a\s*\d+",           // "cirugía 1", "cirugia 2"
            @"(primera|segunda|tercera)\s+cirug[íi]a",  // "primera cirugía"
            @"(cers|mld|adenoides|am[íi]gdalas)",       // nombres de cirugías
            @"modificar|cambiar|editar",                 // comandos de edición
            @"(hora|lugar|cirujano|aneste|cantidad)\s", // campos a modificar
            @"la\s+(primera|segunda|tercera|última)"     // "la primera", "la segunda"
        };

        return editPatterns.Any(pattern => 
            System.Text.RegularExpressions.Regex.IsMatch(inputLower, pattern));
    }

    private async Task HandleMultipleSurgeryEdit(ITelegramBotClient bot, Appointment markerAppt, string editCommand, long chatId, CancellationToken ct)
    {
        try
        {
            // Extraer número de cirugías del marker
            var countStr = markerAppt.Notas?.Split(':')[1];
            if (!int.TryParse(countStr, out var surgeryCount))
            {
                await MessageSender.SendWithRetry(chatId, "❌ Error procesando edición múltiple.", cancellationToken: ct);
                return;
            }

            // Cargar todas las cirugías guardadas
            var surgeries = new List<Appointment>();
            for (int i = 1; i <= surgeryCount; i++)
            {
                var tempChatId = chatId + i * 100;
                var savedAppt = _stateManager.GetOrCreateAppointment(tempChatId);
                if (!string.IsNullOrWhiteSpace(savedAppt.Cirugia))
                {
                    savedAppt.Notas = $"Cirugía {i}"; // Para identificación
                    surgeries.Add(savedAppt);
                }
            }

            if (surgeries.Count == 0)
            {
                await MessageSender.SendWithRetry(chatId, "❌ No se encontraron cirugías para editar.", cancellationToken: ct);
                return;
            }

            // Parsear comando de edición
            var editResult = ParseMultipleSurgeryEditCommand(editCommand, surgeries);

            if (editResult.Success)
            {
                // Aplicar la edición
                await ApplyMultipleSurgeryEdit(bot, editResult, surgeries, chatId, ct);
            }
            else
            {
                // Mostrar opciones de edición disponibles
                await ShowMultipleSurgeryEditOptions(bot, surgeries, chatId, ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-EDIT] Error handling multiple surgery edit: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error procesando edición. Intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private MultiSurgeryEditResult ParseMultipleSurgeryEditCommand(string editCommand, List<Appointment> surgeries)
    {
        var inputLower = editCommand.ToLowerInvariant();
        var result = new MultiSurgeryEditResult();

        try
        {
            // Patron 1: "cirugía 2 hora 16hs"
            var pattern1 = @"cirug[íi]a\s*(\d+)\s+(\w+)\s+(.+)";
            var match1 = System.Text.RegularExpressions.Regex.Match(inputLower, pattern1);
            if (match1.Success)
            {
                var surgeryIndex = int.Parse(match1.Groups[1].Value) - 1; // Convert to 0-based
                var fieldName = match1.Groups[2].Value;
                var newValue = match1.Groups[3].Value;

                if (surgeryIndex >= 0 && surgeryIndex < surgeries.Count)
                {
                    result.Success = true;
                    result.SurgeryIndex = surgeryIndex;
                    result.FieldName = MapFieldName(fieldName);
                    result.NewValue = newValue;
                    return result;
                }
            }

            // Patrón 2: "MLD hora 16hs" (por nombre de cirugía)
            var pattern2 = @"(cers|mld|adenoides|am[íi]gdalas|amigdalas)\s+(\w+)\s+(.+)";
            var match2 = System.Text.RegularExpressions.Regex.Match(inputLower, pattern2);
            if (match2.Success)
            {
                var surgeryName = match2.Groups[1].Value;
                var fieldName = match2.Groups[2].Value;
                var newValue = match2.Groups[3].Value;

                // Buscar cirugía por nombre
                for (int i = 0; i < surgeries.Count; i++)
                {
                    if (surgeries[i].Cirugia?.ToLowerInvariant().Contains(surgeryName) == true)
                    {
                        result.Success = true;
                        result.SurgeryIndex = i;
                        result.FieldName = MapFieldName(fieldName);
                        result.NewValue = newValue;
                        return result;
                    }
                }
            }

            // Patrón 3: "primera cirugía lugar Hospital"
            var pattern3 = @"(primera|segunda|tercera)\s+cirug[íi]a\s+(\w+)\s+(.+)";
            var match3 = System.Text.RegularExpressions.Regex.Match(inputLower, pattern3);
            if (match3.Success)
            {
                var position = match3.Groups[1].Value;
                var fieldName = match3.Groups[2].Value;
                var newValue = match3.Groups[3].Value;

                var surgeryIndex = position switch
                {
                    "primera" => 0,
                    "segunda" => 1,
                    "tercera" => 2,
                    _ => -1
                };

                if (surgeryIndex >= 0 && surgeryIndex < surgeries.Count)
                {
                    result.Success = true;
                    result.SurgeryIndex = surgeryIndex;
                    result.FieldName = MapFieldName(fieldName);
                    result.NewValue = newValue;
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-EDIT] Error parsing edit command: {ex}");
        }

        return result;
    }

    private string MapFieldName(string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "hora" or "tiempo" => "hora",
            "fecha" => "fecha", 
            "lugar" or "hospital" or "clinica" => "lugar",
            "cirujano" or "doctor" or "medico" => "cirujano",
            "aneste" or "anestesiologo" or "anestesiologist" => "anestesiologo",
            "cantidad" or "qty" => "cantidad",
            _ => fieldName.ToLowerInvariant()
        };
    }

    private async Task ApplyMultipleSurgeryEdit(ITelegramBotClient bot, MultiSurgeryEditResult editResult, List<Appointment> surgeries, long chatId, CancellationToken ct)
    {
        try
        {
            var targetSurgery = surgeries[editResult.SurgeryIndex];
            var oldValue = "";
            var fieldDisplayName = "";

            // Aplicar la edición según el campo
            switch (editResult.FieldName)
            {
                case "hora":
                    oldValue = targetSurgery.FechaHora?.ToString("HH:mm") ?? "sin definir";
                    if (TryParseTime(editResult.NewValue, out var newTime))
                    {
                        var baseDate = targetSurgery.FechaHora?.Date ?? DateTime.Today;
                        targetSurgery.FechaHora = baseDate.Add(newTime);
                        fieldDisplayName = "Hora";
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId, $"❌ Formato de hora inválido: {editResult.NewValue}", cancellationToken: ct);
                        return;
                    }
                    break;

                case "lugar":
                    oldValue = targetSurgery.Lugar ?? "sin definir";
                    targetSurgery.Lugar = editResult.NewValue;
                    fieldDisplayName = "Lugar";
                    break;

                case "cirujano":
                    oldValue = targetSurgery.Cirujano ?? "sin definir";
                    targetSurgery.Cirujano = editResult.NewValue;
                    fieldDisplayName = "Cirujano";
                    break;

                case "anestesiologo":
                    oldValue = targetSurgery.Anestesiologo ?? "sin definir";
                    targetSurgery.Anestesiologo = editResult.NewValue;
                    fieldDisplayName = "Anestesiólogo";
                    break;

                case "cantidad":
                    oldValue = targetSurgery.Cantidad?.ToString() ?? "sin definir";
                    if (int.TryParse(editResult.NewValue, out var newQty))
                    {
                        targetSurgery.Cantidad = newQty;
                        fieldDisplayName = "Cantidad";
                    }
                    else
                    {
                        await MessageSender.SendWithRetry(chatId, $"❌ Cantidad inválida: {editResult.NewValue}", cancellationToken: ct);
                        return;
                    }
                    break;

                default:
                    await MessageSender.SendWithRetry(chatId, $"❌ Campo no reconocido: {editResult.FieldName}", cancellationToken: ct);
                    return;
            }

            // Guardar la cirugía modificada
            var tempChatId = chatId + (editResult.SurgeryIndex + 1) * 100;
            _stateManager.SetAppointment(tempChatId, targetSurgery);

            // Confirmar la edición
            await MessageSender.SendWithRetry(chatId,
                $"✅ **Cirugía {editResult.SurgeryIndex + 1}** modificada:\n" +
                $"🔹 {fieldDisplayName}: `{oldValue}` → `{editResult.NewValue}`\n\n" +
                $"📋 **{targetSurgery.Cantidad} {targetSurgery.Cirugia?.ToUpper()}**\n" +
                $"📅 {targetSurgery.FechaHora:dddd, dd MMMM yyyy HH:mm}\n" +
                $"🏥 {targetSurgery.Lugar}\n" +
                $"👨‍⚕️ {targetSurgery.Cirujano}\n" +
                $"💉 {targetSurgery.Anestesiologo}",
                cancellationToken: ct);

            // Mostrar resumen actualizado
            await ShowUpdatedMultipleSurgeriesSummary(bot, chatId, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-EDIT] Error applying edit: {ex}");
            await MessageSender.SendWithRetry(chatId, "❌ Error aplicando la edición.", cancellationToken: ct);
        }
    }

    private bool TryParseTime(string timeStr, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        
        try
        {
            // Limpiar y normalizar
            timeStr = timeStr.ToLowerInvariant()
                            .Replace("hs", "")
                            .Replace("h", "")
                            .Replace(".", ":")
                            .Trim();

            // Intentar parsear directamente
            if (TimeSpan.TryParse(timeStr, out time))
                return true;

            // Intentar formato de una sola cifra (ej: "14" -> "14:00")
            if (int.TryParse(timeStr, out var hour) && hour >= 0 && hour <= 23)
            {
                time = new TimeSpan(hour, 0, 0);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task ShowMultipleSurgeryEditOptions(ITelegramBotClient bot, List<Appointment> surgeries, long chatId, CancellationToken ct)
    {
        var message = "✏️ **OPCIONES DE EDICIÓN**\n\n" +
                     "📋 **Cirugías actuales:**\n";

        for (int i = 0; i < surgeries.Count; i++)
        {
            var surgery = surgeries[i];
            message += $"**{i + 1}. {surgery.Cantidad} {surgery.Cirugia?.ToUpper()}**\n" +
                      $"   📅 {surgery.FechaHora:HH:mm} - 🏥 {surgery.Lugar}\n" +
                      $"   👨‍⚕️ {surgery.Cirujano} - 💉 {surgery.Anestesiologo}\n\n";
        }

        message += "🛠️ **Comandos de edición:**\n" +
                   "• `cirugía 1 hora 16hs` - Cambiar hora de cirugía específica\n" +
                   "• `MLD lugar Hospital Italiano` - Cambiar por nombre\n" +
                   "• `primera cirugía anestesiólogo López` - Cambiar por posición\n" +
                   "• `CERS cirujano Rodriguez` - Cambiar por tipo\n\n" +
                   "📝 **Campos editables:** hora, lugar, cirujano, anestesiologo, cantidad";

        await MessageSender.SendWithRetry(chatId, message, cancellationToken: ct);
    }

    private async Task ShowUpdatedMultipleSurgeriesSummary(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        // Obtener el marker para saber cuántas cirugías hay
        var markerAppt = _stateManager.GetOrCreateAppointment(chatId);
        if (string.IsNullOrWhiteSpace(markerAppt.Notas) || !markerAppt.Notas.StartsWith("MARKER_MULTIPLE_SURGERIES:"))
            return;

        var countStr = markerAppt.Notas.Split(':')[1];
        if (!int.TryParse(countStr, out var surgeryCount))
            return;

        var summary = "📋 **RESUMEN ACTUALIZADO**\n\n";
        
        for (int i = 1; i <= surgeryCount; i++)
        {
            var tempChatId = chatId + i * 100;
            var surgery = _stateManager.GetOrCreateAppointment(tempChatId);
            
            if (!string.IsNullOrWhiteSpace(surgery.Cirugia))
            {
                summary += $"**{i}. {surgery.Cantidad} {surgery.Cirugia?.ToUpper()}**\n" +
                          $"📅 {surgery.FechaHora:dddd, dd MMMM yyyy HH:mm}\n" +
                          $"🏥 {surgery.Lugar}\n" +
                          $"👨‍⚕️ {surgery.Cirujano}\n" +
                          $"💉 {surgery.Anestesiologo}\n\n";
            }
        }

        summary += "🚀 **¿Confirmar TODAS las cirugías?** Responde **'si'** para proceder o edita otra cirugía.";

        await MessageSender.SendWithRetry(chatId, summary, cancellationToken: ct);
    }

    public class MultiSurgeryEditResult
    {
        public bool Success { get; set; }
        public int SurgeryIndex { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }

    private async Task<bool> HandleEmailCapture(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        if (appt.CampoQueFalta == Appointment.CampoPendiente.EsperandoEmailAnestesiologo)
        {
            Console.WriteLine($"[FLOW] Handling email capture for: {rawText}");
            return await _confirmationService.HandleEmailResponse(bot, appt, rawText, chatId, ct);
        }
        return false;
    }

    private async Task HandleMultipleSurgeries(ITelegramBotClient bot, MultiSurgeryParser.ParseResult parseResult, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[MULTI-SURGERY] Processing {parseResult.IndividualInputs.Count} surgeries for chat {chatId}");

            // Informar al usuario que se detectaron múltiples cirugías
            await MessageSender.SendWithRetry(chatId,
                $"🔍 Detecté {parseResult.IndividualInputs.Count} cirugías diferentes:\n" +
                string.Join("\n", parseResult.DetectedSurgeries.Select((s, i) => $"{i + 1}. {s.Quantity} {s.SurgeryName}")) +
                "\n\nProcesando cada una...",
                cancellationToken: ct);

            var processedAppointments = new List<Appointment>();
            var errors = new List<string>();

            // Procesar cada cirugía individualmente
            for (int i = 0; i < parseResult.IndividualInputs.Count; i++)
            {
                try
                {
                    var individualInput = parseResult.IndividualInputs[i];
                    var surgeryInfo = parseResult.DetectedSurgeries[i];
                    
                    Console.WriteLine($"[MULTI-SURGERY] Processing surgery {i + 1}: {individualInput}");

                    // Crear un contexto temporal para esta cirugía (usar el chatId real)
                    var tempAppt = new Appointment { ChatId = chatId };
                    tempAppt.HistoricoInputs.Add(individualInput);

                    // Procesar con LLM usando el chatId real pero sin guardar en el state manager
                    await ProcessSingleSurgeryWithLLM(bot, tempAppt, individualInput, chatId, ct);

                    // Verificar si el procesamiento fue exitoso
                    if (!string.IsNullOrWhiteSpace(tempAppt.Cirugia))
                    {
                        processedAppointments.Add(tempAppt);
                        
                        await MessageSender.SendWithRetry(chatId,
                            $"✅ Cirugía {i + 1} procesada: {tempAppt.Cantidad} {tempAppt.Cirugia?.ToUpper()}",
                            cancellationToken: ct);
                    }
                    else
                    {
                        errors.Add($"Cirugía {i + 1}: {surgeryInfo.SurgeryName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MULTI-SURGERY] Error processing surgery {i + 1}: {ex}");
                    errors.Add($"Cirugía {i + 1}: Error de procesamiento");
                }
            }

            // Mostrar resumen final
            if (processedAppointments.Count > 0)
            {
                await ShowMultipleSurgeriesSummary(bot, processedAppointments, chatId, ct);
            }

            if (errors.Count > 0)
            {
                await MessageSender.SendWithRetry(chatId,
                    $"⚠️ Algunas cirugías no se pudieron procesar:\n{string.Join("\n", errors)}",
                    cancellationToken: ct);
            }

            // Limpiar el contexto original después de procesar múltiples cirugías
            _stateManager.ClearContext(chatId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-SURGERY] Error handling multiple surgeries: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando múltiples cirugías. Por favor, intenta enviar una cirugía a la vez.",
                cancellationToken: ct);
        }
    }

    private async Task ShowMultipleSurgeriesSummary(ITelegramBotClient bot, List<Appointment> appointments, long chatId, CancellationToken ct)
    {
        var summary = "📋 **RESUMEN DE CIRUGÍAS PROCESADAS**\n\n";
        
        for (int i = 0; i < appointments.Count; i++)
        {
            var appt = appointments[i];
            summary += $"**{i + 1}. {appt.Cantidad} {appt.Cirugia?.ToUpper()}**\n" +
                      $"📅 {appt.FechaHora:dd/MM/yyyy HH:mm}\n" +
                      $"🏥 {appt.Lugar}\n" +
                      $"👨‍⚕️ {appt.Cirujano}\n" +
                      $"💉 {appt.Anestesiologo}\n\n";
        }

        summary += $"🔥 **Total: {appointments.Count} cirugías**\n\n" +
                   "¿Querés confirmar todas estas cirugías? Responde **'confirmar todas'** o **'si'** para proceder.";

        await MessageSender.SendWithRetry(chatId, summary, cancellationToken: ct);

        // Guardar los appointments para confirmación posterior
        // Podrías usar un diccionario temporal o una propiedad del state manager
        foreach (var appt in appointments)
        {
            appt.ConfirmacionPendiente = true;
            // Temporalmente guardamos en contextos con IDs únicos
            var contextId = chatId + (appointments.IndexOf(appt) * 100000);
            _stateManager.SetAppointment(contextId, appt);
        }
    }

    private async Task HandleMultipleSurgeriesAfterValidation(ITelegramBotClient bot, MultiSurgeryParser.ParseResult parseResult, Appointment validatedAppt, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[MULTI-SURGERY-VALIDATED] Processing {parseResult.DetectedSurgeries.Count} surgeries with validated data");

            // Informar al usuario sobre las múltiples cirugías detectadas
            await MessageSender.SendWithRetry(chatId,
                $"🔍 **¡Detecté {parseResult.DetectedSurgeries.Count} cirugías diferentes!**\n\n" +
                string.Join("\n", parseResult.DetectedSurgeries.Select((s, i) => $"{i + 1}. **{s.Quantity} {s.SurgeryName.ToUpper()}**")) +
                "\n\n✅ Todos los datos están completos, creando cada cirugía...",
                cancellationToken: ct);

            var processedAppointments = new List<Appointment>();
            var errors = new List<string>();

            // Crear cada cirugía basada en los datos ya validados
            for (int i = 0; i < parseResult.DetectedSurgeries.Count; i++)
            {
                try
                {
                    var surgeryInfo = parseResult.DetectedSurgeries[i];
                    
                    // Crear nuevo appointment copiando todos los datos validados
                    var newAppt = new Appointment
                    {
                        ChatId = chatId,
                        FechaHora = validatedAppt.FechaHora,
                        Lugar = validatedAppt.Lugar,
                        Cirujano = validatedAppt.Cirujano,
                        Anestesiologo = validatedAppt.Anestesiologo,
                        
                        // Datos específicos de esta cirugía
                        Cirugia = surgeryInfo.SurgeryName,
                        Cantidad = surgeryInfo.Quantity,
                        
                        // Notas indicando que es parte de múltiples cirugías
                        Notas = $"Cirugía {i + 1} de {parseResult.DetectedSurgeries.Count}"
                    };

                    processedAppointments.Add(newAppt);
                    
                    await MessageSender.SendWithRetry(chatId,
                        $"✅ **Cirugía {i + 1}:** {newAppt.Cantidad} {newAppt.Cirugia?.ToUpper()}",
                        cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MULTI-SURGERY-VALIDATED] Error creating surgery {i + 1}: {ex}");
                    errors.Add($"Cirugía {i + 1}: {parseResult.DetectedSurgeries[i].SurgeryName}");
                }
            }

            // Mostrar resumen final y proceder con confirmación
            if (processedAppointments.Count > 0)
            {
                await ShowFinalMultipleSurgeriesSummary(bot, processedAppointments, chatId, ct);
            }

            if (errors.Count > 0)
            {
                await MessageSender.SendWithRetry(chatId,
                    $"⚠️ Algunas cirugías no se pudieron crear:\n{string.Join("\n", errors)}",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MULTI-SURGERY-VALIDATED] Error handling validated multiple surgeries: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando las múltiples cirugías. Intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private async Task ShowFinalMultipleSurgeriesSummary(ITelegramBotClient bot, List<Appointment> appointments, long chatId, CancellationToken ct)
    {
        var summary = "🎯 **RESUMEN FINAL DE CIRUGÍAS**\n\n";
        
        for (int i = 0; i < appointments.Count; i++)
        {
            var appt = appointments[i];
            summary += $"**{i + 1}. {appt.Cantidad} {appt.Cirugia?.ToUpper()}**\n" +
                      $"📅 {appt.FechaHora:dddd, dd MMMM yyyy HH:mm}\n" +
                      $"🏥 {appt.Lugar}\n" +
                      $"👨‍⚕️ {appt.Cirujano}\n" +
                      $"💉 {appt.Anestesiologo}\n\n";
        }

        summary += $"🔥 **Total: {appointments.Count} cirugías programadas**";

        // En lugar de texto simple, usar botones de edición rápida para múltiples cirugías
        // Nota: Para múltiples cirugías, por ahora seguimos con el método tradicional
        // TODO: Implementar botones individuales para cada cirugía
        await MessageSender.SendWithRetry(chatId, summary + "\n\n🚀 **¿Confirmar TODAS las cirugías?**\nResponde **'confirmar todas'** o **'si'** para crear todas.", cancellationToken: ct);

        // Guardar todas las cirugías temporalmente para confirmación global
        for (int i = 0; i < appointments.Count; i++)
        {
            var appt = appointments[i];
            appt.ConfirmacionPendiente = true;
            
            // Usar IDs únicos para cada cirugía
            var tempChatId = chatId + (i + 1) * 100;
            _stateManager.SetAppointment(tempChatId, appt);
        }

        // Marcar que hay múltiples cirugías pendientes de confirmación
        var markerAppt = new Appointment 
        { 
            ChatId = chatId,
            ConfirmacionPendiente = true,
            Notas = $"MARKER_MULTIPLE_SURGERIES:{appointments.Count}"
        };
        _stateManager.SetAppointment(chatId, markerAppt);
    }

    private async Task ProcessSingleSurgeryWithLLM(ITelegramBotClient bot, Appointment tempAppt, string individualInput, long chatId, CancellationToken ct)
    {
        try
        {
            // NO enviar "Procesando..." aquí ya que se envía al inicio del flujo principal
            Console.WriteLine($"[LLM-PROCESSOR] Processing input for surgery: {individualInput}");

            // Llamar directamente al LLM sin pasar por el FlowLLMProcessor que envía mensajes
            var llmResponse = await CallLLMDirectly(individualInput);
            
            if (!string.IsNullOrWhiteSpace(llmResponse))
            {
                // Parsear la respuesta del LLM y aplicar al appointment temporal
                ParseLLMResponse(tempAppt, llmResponse);
                Console.WriteLine($"[LLM-PROCESSOR] ✅ Surgery processed successfully");
            }
            else
            {
                Console.WriteLine($"[LLM-PROCESSOR] ❌ Empty response from LLM");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM-PROCESSOR] Error processing single surgery: {ex}");
            throw; // Re-throw para que el caller maneje el error
        }
    }

    private async Task<string> CallLLMDirectly(string input)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Usar el método que usa assistants (el prompt está ya configurado en el assistant)
            var dict = await _llm.ExtractWithPublishedPromptAsync(input, DateTime.Today);
            var duration = DateTime.UtcNow - startTime;
            
            if (dict != null && dict.Count > 0)
            {
                // Log successful parsing
                await _analytics.LogParsingSuccessAsync(input, dict);
                await _analytics.LogParsingPerformanceAsync("llm_extraction", duration, true);
                // Convertir el dictionary a JSON string para parsing
                var jsonParts = new List<string>();
                foreach (var kvp in dict)
                {
                    jsonParts.Add($"\"{kvp.Key}\": \"{kvp.Value?.Replace("\"", "\\\"")}\"");
                }
                
                var jsonResponse = "{" + string.Join(", ", jsonParts) + "}";
                return jsonResponse;
            }
            else
            {
                // Log empty response as warning
                await _analytics.LogParsingWarningAsync("empty_llm_response", input, "LLM returned empty or null response");
                await _analytics.LogParsingPerformanceAsync("llm_extraction", duration, false);
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM-DIRECT] Error calling LLM: {ex}");
            var duration = DateTime.UtcNow - startTime;
            await _analytics.LogParsingErrorAsync("llm_extraction_exception", input, ex.Message);
            await _analytics.LogParsingPerformanceAsync("llm_extraction", duration, false);
            return string.Empty;
        }
    }

    private void ParseLLMResponse(Appointment appt, string llmResponse)
    {
        try
        {
            // Simple JSON parsing - en production podrías usar System.Text.Json
            var jsonData = llmResponse.Trim();
            if (!jsonData.StartsWith("{") || !jsonData.EndsWith("}"))
            {
                Console.WriteLine("[LLM-PARSER] Response is not valid JSON format");
                return;
            }

            // Extraer campos básicos usando parsing simple
            ExtractJsonField(appt, jsonData, "dia", (value) => {
                if (int.TryParse(value, out var day)) appt.DiaExtraido = day;
            });
            
            ExtractJsonField(appt, jsonData, "mes", (value) => {
                if (int.TryParse(value, out var month)) appt.MesExtraido = month;
            });
            
            ExtractJsonField(appt, jsonData, "anio", (value) => {
                if (int.TryParse(value, out var year)) appt.AnioExtraido = year;
            });
            
            ExtractJsonField(appt, jsonData, "hora", (value) => {
                if (TimeSpan.TryParse(value, out var time)) 
                {
                    appt.HoraExtraida = time.Hours;
                    appt.MinutoExtraido = time.Minutes;
                }
            });
            
            ExtractJsonField(appt, jsonData, "lugar", (value) => appt.Lugar = value);
            ExtractJsonField(appt, jsonData, "cirujano", (value) => appt.Cirujano = value);
            ExtractJsonField(appt, jsonData, "cirugia", (value) => appt.Cirugia = value);
            ExtractJsonField(appt, jsonData, "anestesiologo", (value) => appt.Anestesiologo = value);
            ExtractJsonField(appt, jsonData, "cantidad", (value) => {
                if (int.TryParse(value, out var qty)) appt.Cantidad = qty;
            });
            
            // Intentar completar la fecha/hora si es posible
            appt.TryCompletarFechaHora();
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM-PARSER] Error parsing LLM response: {ex}");
        }
    }

    private void ExtractJsonField(Appointment appt, string json, string fieldName, Action<string> setter)
    {
        try
        {
            var pattern = $"\"{fieldName}\"\\s*:\\s*\"([^\"]*?)\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                setter(match.Groups[1].Value.Trim());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM-PARSER] Error extracting field {fieldName}: {ex}");
        }
    }

    #region Modification Handlers

    private async Task HandleModificationAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        try
        {
            // 1. Buscar appointment(s) que coincidan
            var searchResult = await _searchService.FindCandidatesAsync(chatId, rawText, DateTime.Today);
            
            // 2. Manejar casos ambiguos o no encontrados
            if (!await _updateCoordinator.HandleAmbiguousSearch(searchResult, rawText, chatId, ct))
            {
                return;
            }
            
            // 3. Una sola coincidencia encontrada
            var appointment = searchResult.SingleResult!;
            
            // 4. Crear una copia del appointment para procesamiento con LLM
            var appointmentCopia = new Appointment
            {
                // Copiar TODOS los datos del appointment original
                Id = appointment.Id,
                ChatId = appointment.ChatId,
                EquipoId = appointment.EquipoId,
                GoogleEmail = appointment.GoogleEmail,
                FechaHora = appointment.FechaHora,
                Lugar = appointment.Lugar,
                Cirujano = appointment.Cirujano,
                Cirugia = appointment.Cirugia,
                Cantidad = appointment.Cantidad,
                Anestesiologo = appointment.Anestesiologo,
                Notas = appointment.Notas,
                CalendarEventId = appointment.CalendarEventId,
                CalendarSyncedAt = appointment.CalendarSyncedAt,
                ReminderSentAt = appointment.ReminderSentAt,
                ConfirmacionPendiente = false // Para evitar que se procese como nueva cirugía
            };
            
            Console.WriteLine($"[MODIFICATION] Processing modification with LLM for input: {rawText}");
            
            // 5. Procesar modificación usando el método especializado que preserva campos existentes
            await _llmProcessor.ProcessModificationWithLLM(bot, appointmentCopia, rawText, chatId, ct);
            
            // appointmentCopia ahora contiene los datos originales + modificaciones aplicadas
            
            // 6. Mostrar resumen de cambios
            var summary = GenerateModificationSummary(appointment, appointmentCopia);
            summary += "\n\n¿Confirmar estos cambios? (sí/no)";
            
            await MessageSender.SendWithRetry(chatId, summary, cancellationToken: ct);
            
            // 7. Guardar en contexto para confirmación
            var modificationContext = new ModificationContext
            {
                OriginalAppointment = appointment,
                ModifiedAppointment = appointmentCopia,
                IsAwaitingConfirmation = true
            };
            
            // Usar el appointment en pending para guardar el contexto
            var contextAppt = _stateManager.GetOrCreateAppointment(chatId);
            contextAppt.ModificationContext = modificationContext;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MODIFICATION] Error: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando la modificación. Por favor, intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private async Task HandleCancellationAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        await MessageSender.SendWithRetry(chatId,
            "🚧 La funcionalidad de cancelación está en desarrollo. Por ahora podés modificar la cirugía o contactar directamente.",
            cancellationToken: ct);
    }

    private string GenerateModificationSummary(Appointment original, Appointment modified)
    {
        var summary = "📝 *Cambios solicitados:*\n\n";
        var hasChanges = false;

        // Comparar fecha y hora
        if (original.FechaHora != modified.FechaHora)
        {
            var originalDateTime = original.FechaHora?.ToString("dd/MM/yyyy HH:mm") ?? "No definida";
            var modifiedDateTime = modified.FechaHora?.ToString("dd/MM/yyyy HH:mm") ?? "No definida";
            summary += $"📅 Fecha/Hora: {originalDateTime} → *{modifiedDateTime}*\n";
            hasChanges = true;
        }

        // Comparar lugar
        if (original.Lugar != modified.Lugar)
        {
            summary += $"📍 Lugar: {original.Lugar ?? "No definido"} → *{modified.Lugar ?? "No definido"}*\n";
            hasChanges = true;
        }

        // Comparar cirujano
        if (original.Cirujano != modified.Cirujano)
        {
            summary += $"👨‍⚕️ Cirujano: {original.Cirujano ?? "No definido"} → *{modified.Cirujano ?? "No definido"}*\n";
            hasChanges = true;
        }

        // Comparar cirugía
        if (original.Cirugia != modified.Cirugia)
        {
            summary += $"🏥 Cirugía: {original.Cirugia ?? "No definida"} → *{modified.Cirugia ?? "No definida"}*\n";
            hasChanges = true;
        }

        // Comparar cantidad
        if (original.Cantidad != modified.Cantidad)
        {
            summary += $"🔢 Cantidad: {original.Cantidad} → *{modified.Cantidad}*\n";
            hasChanges = true;
        }

        // Comparar anestesiólogo
        if (original.Anestesiologo != modified.Anestesiologo)
        {
            summary += $"💉 Anestesiólogo: {original.Anestesiologo ?? "No definido"} → *{modified.Anestesiologo ?? "No definido"}*\n";
            hasChanges = true;
        }

        if (!hasChanges)
        {
            summary = "ℹ️ No se detectaron cambios en los datos de la cirugía.";
        }

        return summary;
    }

    private async Task HandleQueryAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        try
        {
            // Buscar appointments que coincidan
            var searchResult = await _searchService.FindCandidatesAsync(chatId, rawText, DateTime.Today);
            
            if (searchResult.NotFound)
            {
                await MessageSender.SendWithRetry(chatId,
                    "❌ No encontré cirugías que coincidan con tu consulta.",
                    cancellationToken: ct);
                return;
            }
            
            var message = searchResult.IsAmbiguous ? 
                "📋 Encontré estas cirugías:\n\n" : 
                "📋 Información de la cirugía:\n\n";
            
            foreach (var appointment in searchResult.Candidates)
            {
                message += $"📅 {appointment.FechaHora?.ToString("dd/MM/yyyy HH:mm")}\n";
                message += $"📍 {appointment.Lugar}\n";
                message += $"👨‍⚕️ {appointment.Cirujano}\n";
                message += $"🏥 {appointment.Cirugia} (x{appointment.Cantidad})\n";
                
                if (!string.IsNullOrEmpty(appointment.Anestesiologo))
                    message += $"💉 {appointment.Anestesiologo}\n";
                
                message += "\n";
            }
            
            await MessageSender.SendWithRetry(chatId, message, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QUERY] Error: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Hubo un error procesando tu consulta.",
                cancellationToken: ct);
        }
    }

    #endregion

    // Métodos públicos para gestión
    public void ReiniciarConversacion(long chatId) => _stateManager.ClearContext(chatId);

    /// <summary>
    /// Maneja mensajes cuando hay un contexto conversacional activo
    /// </summary>
    private async Task HandleWithActiveContext(ITelegramBotClient bot, long chatId, string rawText, Appointment appt, ConversationContext context, CancellationToken ct)
    {
        try
        {
            // Agregar el input al historial
            appt.HistoricoInputs.Add(rawText);
            
            switch (context.Type)
            {
                case ContextType.FieldWizard:
                    // Delegar al wizard handler
                    if (await _wizardHandler.HandleFieldWizard(bot, appt, rawText, chatId, ct))
                    {
                        return; // Wizard manejó el mensaje
                    }
                    
                    // Si wizard no lo manejó, continuar con flujo normal
                    await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                    break;
                    
                case ContextType.Confirming:
                    // Manejar respuestas de confirmación
                    await HandleConfirmationResponse(bot, appt, rawText, chatId, ct);
                    break;
                    
                case ContextType.RegisteringSurgery:
                    // Continuar con registro normal
                    await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                    break;
                    
                case ContextType.ModifyingSurgery:
                    // Manejar modificación en contexto
                    await HandleModificationInContext(bot, appt, rawText, chatId, ct);
                    break;
                    
                default:
                    // Fallback a procesamiento normal
                    await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONTEXT] Error handling message with active context: {ex.Message}");
            
            // Fallback a procesamiento normal
            await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
        }
    }

    /// <summary>
    /// Maneja respuestas cuando el usuario está en modo confirmación
    /// </summary>
    private async Task HandleConfirmationResponse(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        var response = rawText.Trim().ToLowerInvariant();
        
        // Respuestas afirmativas
        if (response is "sí" or "si" or "yes" or "ok" or "confirmar" or "confirmo" or "dale" or "perfecto")
        {
            // Proceder con confirmación
            await _confirmationService.ProcessConfirmationAsync(bot, appt, chatId, ct);
            _stateManager.ClearContext(chatId);
            return;
        }
        
        // Respuestas negativas o de edición
        if (response is "no" or "nope" or "cambiar" or "editar" or "modificar" or "corregir")
        {
            appt.ConfirmacionPendiente = false;
            await MessageSender.SendWithRetry(chatId, 
                "Dale, ¿qué querés cambiar? Podés decirme qué cosa (fecha, lugar, cirujano) o mandarme el dato nuevo.", 
                cancellationToken: ct);
            return;
        }
        
        // Si no es una respuesta clara, recordar el contexto
        await MessageSender.SendWithRetry(chatId,
            $"No entendí \"{rawText}\".\n\n" +
            "Necesito que me confirmes si están bien los datos.\n" +
            "Poneme 'sí' para confirmar o 'no' si querés cambiar algo.",
            cancellationToken: ct);
    }

    /// <summary>
    /// Maneja modificación cuando ya estamos en contexto de modificación
    /// </summary>
    private async Task HandleModificationInContext(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        // Si está en modo de edición de campo específico, usar el wizard
        if (appt.CampoAEditar != Appointment.CampoPendiente.Ninguno)
        {
            // Delegar al message handler para manejar edición
            await _messageHandler.HandleEditMode(bot, appt, rawText, chatId, ct, _llmProcessor);
            return;
        }
        
        // Sino, procesar como nuevo input de modificación
        await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
    }

    /// <summary>
    /// Detecta rápidamente si un mensaje tiene intención de modificación
    /// usando patrones básicos sin LLM para máxima velocidad
    /// </summary>
    private bool IsModificationIntent(string rawText)
    {
        var normalized = rawText.Trim().ToLowerInvariant();
        
        // Patrones claros de modificación
        var modificationPatterns = new[]
        {
            "quiero cambiar",
            "necesito cambiar", 
            "cambiar la cirugia",
            "cambiar cirugia",
            "modificar la cirugia",
            "modificar cirugia",
            "editar la cirugia",
            "editar cirugia",
            "quiero modificar",
            "necesito modificar",
            "cambiar el horario",
            "cambiar la hora",
            "cambiar el lugar",
            "cambiar cirujano",
            "cambiar anestesiologo"
        };
        
        foreach (var pattern in modificationPatterns)
        {
            if (normalized.Contains(pattern))
            {
                Console.WriteLine($"[MODIFICATION-INTENT] Found pattern: '{pattern}' in message");
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Muestra los detalles de la cirugía encontrada y pregunta qué quiere modificar con botones
    /// </summary>
    private async Task ShowAppointmentDetailsAndAskWhatToModify(ITelegramBotClient bot, Appointment appointment, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[MODIFY-DETAILS] Showing appointment details for modification");
            
            // Construir mensaje con detalles actuales
            var details = "✅ <b>Encontré esta cirugía:</b>\n\n";
            details += $"📅 <b>Fecha:</b> {appointment.FechaHora?.ToString("dd/MM/yyyy") ?? "No definida"}\n";
            details += $"⏰ <b>Hora:</b> {appointment.FechaHora?.ToString("HH:mm") ?? "No definida"}\n";
            details += $"🏥 <b>Lugar:</b> {appointment.Lugar ?? "No definido"}\n";
            details += $"👨‍⚕️ <b>Cirujano:</b> {appointment.Cirujano ?? "No definido"}\n";
            details += $"🔬 <b>Cirugía:</b> {appointment.Cirugia ?? "No definida"}\n";
            details += $"🔢 <b>Cantidad:</b> {appointment.Cantidad?.ToString() ?? "1"}\n";
            details += $"💉 <b>Anestesiólogo:</b> {appointment.Anestesiologo ?? "No asignado"}\n\n";
            details += "❓ <b>¿Qué querés cambiar?</b>";

            // Crear teclado con opciones de modificación
            if (_quickEdit != null)
            {
                var keyboard = await _quickEdit.CreateModificationKeyboard(appointment);
                await MessageSender.SendWithRetry(chatId, details, replyMarkup: keyboard, cancellationToken: ct);
            }
            else
            {
                // Fallback sin botones
                details += "\n\n💡 <b>Podés decir:</b>\n";
                details += "• \"cambiar la hora a las 16hs\"\n";
                details += "• \"cambiar el lugar a Anchorena\"\n";
                details += "• \"cambiar cirujano a García\"\n";
                details += "• \"cambiar anestesiólogo\"\n\n";
                details += "❌ Escribí <b>\"cancelar\"</b> para empezar de nuevo.";
                
                await MessageSender.SendWithRetry(chatId, details, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MODIFY-DETAILS] Error showing appointment details: {ex.Message}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error mostrando los detalles. Escribí **\"cancelar\"** para empezar de nuevo.",
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Detecta comandos de cancelación para reiniciar el contexto
    /// </summary>
    private bool IsCancelCommand(string rawText)
    {
        var normalized = rawText.Trim().ToLowerInvariant();
        
        var cancelPatterns = new[]
        {
            "cancelar",
            "cancela",
            "cancel",
            "salir",
            "salí",
            "exit",
            "stop",
            "para",
            "parar",
            "empezar de nuevo",
            "empezar otra vez",
            "reiniciar",
            "restart"
        };
        
        foreach (var pattern in cancelPatterns)
        {
            if (normalized == pattern || normalized.Contains($" {pattern} ") || normalized.StartsWith($"{pattern} ") || normalized.EndsWith($" {pattern}"))
            {
                Console.WriteLine($"[CANCEL-COMMAND] Found pattern: '{pattern}' in message");
                return true;
            }
        }
        
        return false;
    }

    public int ObtenerConversacionesActivas() => _stateManager.GetActiveConversationsCount();
    public void LimpiarConversacionesAntiguas(TimeSpan tiempoLimite) => _stateManager.CleanOldConversations(tiempoLimite);
}