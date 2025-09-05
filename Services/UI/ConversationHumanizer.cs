using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RegistroCx.Models;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services.Extraction;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace RegistroCx.Services.UI
{
    /// <summary>
    /// Servicio para humanizar y consolidar las respuestas del bot, 
    /// evitando múltiples mensajes fragmentados y haciendo la conversación más natural
    /// </summary>
    public class ConversationHumanizer
    {
        private readonly ILogger<ConversationHumanizer> _logger;
        private readonly LLMOpenAIAssistant _llm;
        
        // Prompt para humanización de respuestas (a crear en OpenAI)
        private const string HumanizeResponsePromptId = "pmpt_humanize_responses"; // TODO: Crear este prompt
        private const string HumanizeResponsePromptVersion = "1";
        
        public ConversationHumanizer(ILogger<ConversationHumanizer> logger, LLMOpenAIAssistant llm)
        {
            _logger = logger;
            _llm = llm;
        }

        /// <summary>
        /// Consolida y humaniza una respuesta completa basada en el contexto y resultado del procesamiento
        /// </summary>
        public async Task<HumanizedResponse> CreateHumanizedResponseAsync(
            HumanizationContext context, 
            CancellationToken ct = default)
        {
            try
            {
                // Si es una respuesta simple, usar plantillas rápidas
                if (context.ResponseType == ResponseType.SimpleFieldRequest)
                {
                    return CreateSimpleFieldResponse(context);
                }
                
                // Para respuestas complejas, usar LLM humanizador
                if (context.ResponseType == ResponseType.ComplexProcessing)
                {
                    return await CreateHumanizedComplexResponseAsync(context, ct);
                }
                
                // Fallback: consolidar mensajes básicos
                return ConsolidateBasicMessages(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error humanizing response");
                
                // Fallback básico
                return new HumanizedResponse
                {
                    Message = context.FallbackMessage ?? "Te ayudo con eso. ¿Qué necesitás?",
                    ReplyMarkup = context.ReplyMarkup
                };
            }
        }

        /// <summary>
        /// Crea respuesta simple y directa para solicitud de campos faltantes
        /// </summary>
        private HumanizedResponse CreateSimpleFieldResponse(HumanizationContext context)
        {
            var fieldName = context.MissingField?.ToLowerInvariant();
            
            var message = fieldName switch
            {
                "lugar" => GeneratePlaceRequestMessage(context),
                "fechahora" => "🗓️ ¡Perfecto! ¿Para cuándo es? Decime la fecha y hora.",
                "cirujano" => "👨‍⚕️ ¡Dale! ¿Con qué cirujano?",
                "anestesiologo" => "💉 ¿Quién va de anestesiólogo?",
                "cantidad" => "🔢 ¿Cuántas son en total?",
                _ => $"✅ ¡Perfecto! Me falta {GetFieldHumanName(fieldName)}. ¿Me lo podés pasar?"
            };

            return new HumanizedResponse
            {
                Message = message,
                ReplyMarkup = context.ReplyMarkup
            };
        }

        /// <summary>
        /// Genera mensaje personalizado para solicitar lugar basado en contexto
        /// </summary>
        private string GeneratePlaceRequestMessage(HumanizationContext context)
        {
            int qty = 1;
            var hasMultiple = context.ProcessedData?.ContainsKey("cantidad") == true && 
                              int.TryParse(context.ProcessedData["cantidad"]?.ToString(), out qty) && qty > 1;
            
            var surgeryType = context.ProcessedData?.ContainsKey("cirugia") == true ? 
                context.ProcessedData["cirugia"]?.ToString() : "";
            
            var doctorName = context.ProcessedData?.ContainsKey("cirujano") == true ? 
                context.ProcessedData["cirujano"]?.ToString() : "";

            if (!string.IsNullOrEmpty(surgeryType) && !string.IsNullOrEmpty(doctorName))
            {
                var pluralText = hasMultiple ? $"{qty} {surgeryType?.ToUpper()}" : surgeryType?.ToUpper();
                return $"🏥 ¡Genial! {pluralText} con {doctorName}. ¿En qué lugar es?";
            }
            
            if (!string.IsNullOrEmpty(surgeryType))
            {
                return $"🏥 ¡Perfecto! {surgeryType.ToUpper()}. ¿En qué hospital o clínica?";
            }

            return "🏥 ¡Dale! ¿En qué lugar es?";
        }

        /// <summary>
        /// Usa LLM para crear respuesta humanizada compleja
        /// </summary>
        private async Task<HumanizedResponse> CreateHumanizedComplexResponseAsync(
            HumanizationContext context, 
            CancellationToken ct)
        {
            try
            {
                var humanizedText = await CallHumanizeResponsePrompt(context, ct);
                
                return new HumanizedResponse
                {
                    Message = humanizedText,
                    ReplyMarkup = context.ReplyMarkup
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calling humanization LLM, using fallback");
                return ConsolidateBasicMessages(context);
            }
        }

        /// <summary>
        /// Consolida múltiples mensajes en uno solo
        /// </summary>
        private HumanizedResponse ConsolidateBasicMessages(HumanizationContext context)
        {
            var parts = new List<string>();
            
            // Agregar confirmación de procesamiento si aplica
            if (context.IsNewSurgery)
            {
                parts.Add("✅ ¡Perfecto! Nueva cirugía registrada.");
            }
            
            // Agregar información procesada
            if (context.ProcessedData?.Any() == true)
            {
                var summary = GenerateDataSummary(context.ProcessedData);
                if (!string.IsNullOrEmpty(summary))
                {
                    parts.Add(summary);
                }
            }
            
            // Agregar solicitud de campo faltante
            if (!string.IsNullOrEmpty(context.MissingField))
            {
                parts.Add($"Me falta {GetFieldHumanName(context.MissingField)}. ¿Me lo podés pasar?");
            }
            
            var message = parts.Count > 0 ? string.Join(" ", parts) : 
                         context.FallbackMessage ?? "¿Cómo te ayudo?";

            return new HumanizedResponse
            {
                Message = message,
                ReplyMarkup = context.ReplyMarkup
            };
        }

        /// <summary>
        /// Genera resumen de datos procesados
        /// </summary>
        private string GenerateDataSummary(Dictionary<string, object> data)
        {
            var parts = new List<string>();
            
            if (data.ContainsKey("cantidad") && data.ContainsKey("cirugia"))
            {
                var qty = data["cantidad"]?.ToString();
                var surgery = data["cirugia"]?.ToString()?.ToUpper();
                parts.Add($"{qty} {surgery}");
            }
            
            if (data.ContainsKey("cirujano"))
            {
                parts.Add($"con {data["cirujano"]}");
            }
            
            if (data.ContainsKey("fechahora"))
            {
                parts.Add($"para {data["fechahora"]}");
            }
            
            return parts.Count > 0 ? $"({string.Join(" ", parts)})" : "";
        }

        /// <summary>
        /// Llama al prompt LLM de humanización
        /// </summary>
        private async Task<string> CallHumanizeResponsePrompt(HumanizationContext context, CancellationToken ct)
        {
            var input = BuildHumanizationInput(context);
            
            var body = new
            {
                prompt = new { id = HumanizeResponsePromptId, version = HumanizeResponsePromptVersion },
                input = input
            };

            var jsonBody = System.Text.Json.JsonSerializer.Serialize(body);
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetOpenAIApiKey());
            
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.openai.com/v1/responses", content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error calling humanization prompt: {response.StatusCode}");
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseHumanizationResponse(responseContent);
        }

        /// <summary>
        /// Construye el input para el prompt de humanización
        /// </summary>
        private string BuildHumanizationInput(HumanizationContext context)
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                scenario = context.ResponseType.ToString(),
                processed_data = context.ProcessedData,
                missing_field = context.MissingField,
                is_new_surgery = context.IsNewSurgery,
                user_input = context.OriginalUserInput,
                conversation_stage = context.ConversationStage
            });
        }

        /// <summary>
        /// Parsea la respuesta del LLM humanizador
        /// </summary>
        private string ParseHumanizationResponse(string response)
        {
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
                
                if (jsonDoc.RootElement.TryGetProperty("output", out var outputArray))
                {
                    foreach (var outEntry in outputArray.EnumerateArray())
                    {
                        if (outEntry.TryGetProperty("type", out var typeProperty) && 
                            typeProperty.GetString() == "message" &&
                            outEntry.TryGetProperty("content", out var contentArray))
                        {
                            foreach (var part in contentArray.EnumerateArray())
                            {
                                if (part.TryGetProperty("type", out var partTypeProperty) &&
                                    partTypeProperty.GetString() == "output_text" &&
                                    part.TryGetProperty("text", out var textProperty))
                                {
                                    return textProperty.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing humanization response");
            }

            return "¿Cómo te ayudo?";
        }

        /// <summary>
        /// Obtiene nombre legible de campo
        /// </summary>
        private string GetFieldHumanName(string? fieldName)
        {
            return fieldName?.ToLowerInvariant() switch
            {
                "lugar" => "el lugar (hospital/clínica)",
                "fechahora" => "la fecha y hora",
                "cirujano" => "el cirujano",
                "anestesiologo" => "el anestesiólogo", 
                "cantidad" => "la cantidad",
                "cirugia" => "el tipo de cirugía",
                _ => "esa información"
            };
        }

        /// <summary>
        /// Obtiene API key de OpenAI
        /// </summary>
        private string GetOpenAIApiKey()
        {
            return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy";
        }
    }

    /// <summary>
    /// Contexto para humanización de respuestas
    /// </summary>
    public class HumanizationContext
    {
        public ResponseType ResponseType { get; set; }
        public string? MissingField { get; set; }
        public Dictionary<string, object>? ProcessedData { get; set; }
        public bool IsNewSurgery { get; set; }
        public string? OriginalUserInput { get; set; }
        public string? ConversationStage { get; set; }
        public string? FallbackMessage { get; set; }
        public ReplyMarkup? ReplyMarkup { get; set; }
    }

    /// <summary>
    /// Respuesta humanizada consolidada
    /// </summary>
    public class HumanizedResponse
    {
        public string Message { get; set; } = "";
        public ReplyMarkup? ReplyMarkup { get; set; }
    }

    /// <summary>
    /// Tipos de respuesta para humanización
    /// </summary>
    public enum ResponseType
    {
        SimpleFieldRequest,      // Solicitar un campo faltante simple
        ComplexProcessing,       // Procesamiento complejo con múltiples elementos  
        Confirmation,           // Confirmación de datos
        Error,                  // Manejo de errores
        BasicResponse          // Respuesta básica
    }
}