using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Services.Extraction;
using RegistroCx.Services.Flow;

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
    private readonly Dictionary<long, Appointment> _pending = new();

    // Helpers especializados
    private readonly FlowStateManager _stateManager;
    private readonly FlowMessageHandler _messageHandler;
    private readonly FlowWizardHandler _wizardHandler;
    private readonly FlowLLMProcessor _llmProcessor;

    public CirugiaFlowService(LLMOpenAIAssistant llm)
    {
        _llm = llm;
        _stateManager = new FlowStateManager(_pending);
        _messageHandler = new FlowMessageHandler();
        _wizardHandler = new FlowWizardHandler();
        _llmProcessor = new FlowLLMProcessor(llm);
    }

    public async Task HandleAsync(ITelegramBotClient bot, long chatId, string rawText, CancellationToken ct)
    {
        // Manejar comandos especiales
        if (await _messageHandler.HandleSpecialCommands(bot, chatId, rawText, ct))
        {
            _stateManager.ClearContext(chatId);
            return;
        }

        // Obtener o crear appointment
        var appt = _stateManager.GetOrCreateAppointment(chatId);
        appt.HistoricoInputs.Add(rawText);

        // Manejar confirmación
        if (await _messageHandler.HandleConfirmation(bot, appt, rawText, chatId, ct))
        {
            if (appt.ConfirmacionPendiente == false && rawText.Trim().ToLowerInvariant() is "si" or "sí" or "ok" or "dale" or "confirmo" or "confirmar")
            {
                _stateManager.ClearContext(chatId);
            }
            return;
        }

        // Manejar modo edición
        if (await _messageHandler.HandleEditMode(bot, appt, rawText, chatId, ct, _llmProcessor))
        {
            return;
        }

        // Manejar wizard de campos
        if (await _wizardHandler.HandleFieldWizard(bot, appt, rawText, chatId, ct))
        {
            return;
        }

        // Manejar cambios directos
        if (await _messageHandler.HandleDirectChanges(bot, appt, rawText, chatId, ct, _llmProcessor))
        {
            return;
        }

        // Procesar con LLM para casos nuevos
        await _llmProcessor.ProcessWithLLM(bot, appt, rawText, chatId, ct);
    }

    // Métodos públicos para gestión
    public void ReiniciarConversacion(long chatId) => _stateManager.ClearContext(chatId);
    public int ObtenerConversacionesActivas() => _stateManager.GetActiveConversationsCount();
    public void LimpiarConversacionesAntiguas(TimeSpan tiempoLimite) => _stateManager.CleanOldConversations(tiempoLimite);
}