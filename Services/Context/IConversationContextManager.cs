using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using RegistroCx.Models;

namespace RegistroCx.Services.Context
{
    public interface IConversationContextManager
    {
        // Analizar si un mensaje es relevante al contexto actual
        Task<ContextRelevance> AnalyzeMessageRelevanceAsync(string message, ConversationContext currentContext, CancellationToken ct = default);
        
        // Manejar desviaciones de contexto
        Task<bool> HandleContextDeviationAsync(ITelegramBotClient bot, long chatId, string message, ConversationContext currentContext, CancellationToken ct = default);
        
        // Generar mensaje de recordatorio de contexto
        string GenerateContextReminderMessage(ConversationContext context);
        
        // Extraer contexto actual de una cita
        ConversationContext ExtractContext(Appointment appointment);
        
        // Determinar si un mensaje requiere clasificación de intent global
        bool ShouldBypassIntentClassification(string message, ConversationContext context);
    }

    public class ContextRelevance
    {
        public bool IsRelevant { get; set; }
        public double ConfidenceScore { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool IsExplicitContextSwitch { get; set; } // "nuevo", "cancelar", etc.
    }

    public class ConversationContext
    {
        public ContextType Type { get; set; }
        public string Details { get; set; } = string.Empty;
        public string CurrentField { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public int MessageCount { get; set; }
        public string LastRelevantMessage { get; set; } = string.Empty;
    }

    public enum ContextType
    {
        None,
        RegisteringSurgery,
        ModifyingSurgery,
        FieldWizard,
        Confirming,
        Reporting,
        Canceling
    }
}