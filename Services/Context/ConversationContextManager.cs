using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Services.Extraction;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Helpers;
using Telegram.Bot.Types.ReplyMarkups;

namespace RegistroCx.Services.Context
{
    public class ConversationContextManager : IConversationContextManager
    {
        private readonly LLMOpenAIAssistant _llm;
        private readonly ILogger<ConversationContextManager> _logger;
        
        // Prompt ID para análisis de contexto conversacional
        private const string ContextAnalysisPromptId = "pmpt_68a10cd97c48819685ba35869b43c3ec031d14b92f3fc512"; // TODO: Reemplazar con el prompt ID real
        private const string ContextAnalysisPromptVersion = "3";
        
        // Prompt ID para detección de nueva cirugía contextual
        private const string NewSurgeryDetectorPromptId = "pmpt_68b8b7467b648195bc54e2e1a6e9ce6707afad5c6e5dca0c"; // TODO: Crear este prompt en OpenAI
        private const string NewSurgeryDetectorPromptVersion = "1";

        // Palabras que indican cambio explícito de contexto
        private readonly HashSet<string> _contextSwitchKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "nuevo", "nueva", "empezar", "comenzar", "iniciar", "start", "restart", "reiniciar",
            "cancelar", "cancel", "parar", "stop", "salir", "exit", "abandonar",
            "modificar", "cambiar", "editar", "update", "modify", "corregir",
            "reporte", "report", "informe", "consulta", "buscar", "ver"
        };

        public ConversationContextManager(LLMOpenAIAssistant llm, ILogger<ConversationContextManager> logger)
        {
            _llm = llm;
            _logger = logger;
        }

        public async Task<ContextRelevance> AnalyzeMessageRelevanceAsync(string message, ConversationContext currentContext, CancellationToken ct = default)
        {
            try
            {
                // 1. Verificar cambio explícito de contexto
                var explicitSwitch = DetectExplicitContextSwitch(message);
                if (explicitSwitch.IsExplicitSwitch)
                {
                    return new ContextRelevance
                    {
                        IsRelevant = false,
                        ConfidenceScore = 0.95,
                        Reason = explicitSwitch.Reason,
                        IsExplicitContextSwitch = true
                    };
                }

                // 2. Si no hay contexto activo, cualquier mensaje es potencialmente relevante
                if (currentContext.Type == ContextType.None)
                {
                    return new ContextRelevance
                    {
                        IsRelevant = true,
                        ConfidenceScore = 1.0,
                        Reason = "No hay contexto activo"
                    };
                }

                // 3. Usar LLM para análisis contextual
                var relevance = await AnalyzeRelevanceWithLLM(message, currentContext, ct);
                return relevance;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing message relevance for message: {Message}", message);
                
                // Fallback: asumir relevancia si hay dudas
                return new ContextRelevance
                {
                    IsRelevant = true,
                    ConfidenceScore = 0.5,
                    Reason = "Error en análisis, asumiendo relevancia"
                };
            }
        }

        public async Task<bool> HandleContextDeviationAsync(ITelegramBotClient bot, long chatId, string message, ConversationContext currentContext, CancellationToken ct = default)
        {
            try
            {
                // Generar mensaje contextual apropiado
                var contextMessage = GenerateContextReminderMessage(currentContext);
                
                // Añadir el mensaje del usuario para referencia
                var fullMessage = $"{contextMessage}\n\n💬 Recibí: \"{message}\"\n\n🤔 ¿Qué querés hacer?";

                // Crear botonera inline mejorada
                var inlineKeyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("✅ Seguir con esto", "context_continue"),
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("🆕 Nueva cirugía", "context_new_surgery")
                    }
                });

                await MessageSender.SendWithRetry(chatId, fullMessage, replyMarkup: inlineKeyboard, cancellationToken: ct);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling context deviation for chat {ChatId}", chatId);
                return false;
            }
        }

        public string GenerateContextReminderMessage(ConversationContext context)
        {
            return context.Type switch
            {
                ContextType.RegisteringSurgery => 
                    $"🏥 Estábamos registrando una cirugía nueva{GetProgressDetails(context)}",
                
                ContextType.ModifyingSurgery => 
                    $"✏️ Estábamos modificando datos de una cirugía{GetProgressDetails(context)}",
                
                ContextType.FieldWizard => 
                    $"📝 Te estaba pidiendo: <b>{GetFieldHumanName(context.CurrentField)}</b>{GetProgressDetails(context)}",
                
                ContextType.Confirming => 
                    $"✅ Estaba esperando tu confirmación de los datos{GetProgressDetails(context)}",
                
                ContextType.Reporting => 
                    $"📊 Estábamos armando un reporte{GetProgressDetails(context)}",
                
                ContextType.Canceling => 
                    $"❌ Estábamos cancelando una cirugía{GetProgressDetails(context)}",
                
                _ => "🤔 Estábamos en una conversación..."
            };
        }

        public ConversationContext ExtractContext(Appointment appointment)
        {
            var context = new ConversationContext
            {
                StartedAt = DateTime.UtcNow, // Simplificado - en producción usar timestamp real
                MessageCount = appointment.HistoricoInputs.Count,
                LastRelevantMessage = appointment.HistoricoInputs.LastOrDefault() ?? ""
            };

            Console.WriteLine($"[EXTRACT-CONTEXT] Appointment state - ConfirmacionPendiente: {appointment.ConfirmacionPendiente}, CampoQueFalta: {appointment.CampoQueFalta}, CampoAEditar: {appointment.CampoAEditar}");

            // Determinar tipo de contexto basado en estado del appointment
            if (appointment.ConfirmacionPendiente)
            {
                context.Type = ContextType.Confirming;
                context.Details = "Esperando confirmación de cirugía completa";
            }
            else if (appointment.CampoQueFalta != Appointment.CampoPendiente.Ninguno)
            {
                context.Type = ContextType.FieldWizard;
                context.CurrentField = appointment.CampoQueFalta.ToString();
                context.Details = $"Esperando campo: {GetFieldHumanName(context.CurrentField)}";
            }
            else if (appointment.CampoAEditar != Appointment.CampoPendiente.Ninguno)
            {
                context.Type = ContextType.ModifyingSurgery;
                context.CurrentField = appointment.CampoAEditar.ToString();
                context.Details = $"Editando campo: {GetFieldHumanName(context.CurrentField)}";
            }
            else if (HasSomeData(appointment))
            {
                context.Type = ContextType.RegisteringSurgery;
                context.Details = "Registrando nueva cirugía";
            }
            else
            {
                context.Type = ContextType.None;
                context.Details = "Sin contexto activo";
            }

            Console.WriteLine($"[EXTRACT-CONTEXT] Extracted context type: {context.Type}, Details: {context.Details}");
            return context;
        }

        public bool ShouldBypassIntentClassification(string message, ConversationContext context)
        {
            Console.WriteLine($"[BYPASS-CHECK] Context type: {context.Type}, Message: '{message}'");
            
            // Si estamos en wizard mode o confirmación, saltear intent classification
            // a menos que sea un cambio explícito de contexto
            if (context.Type == ContextType.FieldWizard || context.Type == ContextType.Confirming)
            {
                Console.WriteLine($"[BYPASS-CHECK] In {context.Type} mode, checking for explicit context switch...");
                var explicitSwitch = DetectExplicitContextSwitch(message);
                var shouldBypass = !explicitSwitch.IsExplicitSwitch;
                Console.WriteLine($"[BYPASS-CHECK] Explicit switch: {explicitSwitch.IsExplicitSwitch}, Should bypass: {shouldBypass}");
                return shouldBypass;
            }

            Console.WriteLine($"[BYPASS-CHECK] Not in wizard/confirming mode, not bypassing");
            return false;
        }

        // ===== MÉTODOS PRIVADOS =====

        private async Task<ContextRelevance> AnalyzeRelevanceWithLLM(string message, ConversationContext context, CancellationToken ct)
        {
            try
            {
                // PRIMERO: Usar el detector especializado de nueva cirugía
                var newSurgeryResult = await DetectNewSurgeryWithLLM(message, context, ct);
                if (newSurgeryResult.IsNewSurgery)
                {
                    return new ContextRelevance
                    {
                        IsRelevant = false,
                        ConfidenceScore = newSurgeryResult.Confidence,
                        Reason = newSurgeryResult.Reason,
                        IsExplicitContextSwitch = true
                    };
                }
                
                // SEGUNDO: Si no es nueva cirugía, analizar relevancia normal
                var analysisInput = BuildContextAnalysisInput(message, context);
                var result = await CallContextAnalysisPrompt(analysisInput, ct);
                
                return ParseLLMContextResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LLM context analysis");
                
                // Fallback simple: usar heurísticas
                return AnalyzeRelevanceWithHeuristics(message, context);
            }
        }

        private string BuildContextAnalysisInput(string message, ConversationContext context)
        {
            var contextDescription = context.Type switch
            {
                ContextType.FieldWizard => $"esperando_campo_{GetFieldHumanName(context.CurrentField).Replace(" ", "_")}",
                ContextType.Confirming => "esperando_confirmacion_cirugia",
                ContextType.RegisteringSurgery => "registrando_nueva_cirugia",
                ContextType.ModifyingSurgery => "modificando_cirugia_existente",
                _ => "conversacion_activa"
            };

            // Crear input estructurado para el prompt de análisis de contexto
            return $@"{{
                ""contexto_actual"": ""{contextDescription}"",
                ""detalle_contexto"": ""{context.Details}"",
                ""mensaje_usuario"": ""{message}"",
                ""timestamp"": ""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}""
            }}";
        }

        /// <summary>
        /// Llama al prompt específico para análisis de contexto conversacional
        /// </summary>
        private async Task<string> CallContextAnalysisPrompt(string inputJson, CancellationToken ct)
        {
            // Usar el patrón estándar de llamada a prompts
            var body = new
            {
                prompt = new { id = ContextAnalysisPromptId, version = ContextAnalysisPromptVersion },
                input = inputJson
            };

            var jsonBody = System.Text.Json.JsonSerializer.Serialize(body);
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetOpenAIApiKey());
            
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.openai.com/v1/responses", content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error calling context analysis prompt: {response.StatusCode} - {errorContent}");
            }
            
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Parsea la respuesta del prompt de análisis de contexto
        /// </summary>
        private ContextRelevance ParseLLMContextResponse(string response)
        {
            try
            {
                // Parsear la respuesta del prompt que debería estar en formato estructurado
                var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
                
                // Buscar en 'output' el message con la respuesta del análisis
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
                                    var assistantText = textProperty.GetString();
                                    return ParseContextAnalysisResult(assistantText);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing LLM context response: {Response}", response);
            }

            // Fallback
            return new ContextRelevance
            {
                IsRelevant = true,
                ConfidenceScore = 0.5,
                Reason = "Error parseando respuesta del prompt",
                IsExplicitContextSwitch = false
            };
        }

        /// <summary>
        /// Parsea el resultado específico del análisis de contexto del assistant
        /// </summary>
        private ContextRelevance ParseContextAnalysisResult(string? assistantText)
        {
            if (string.IsNullOrEmpty(assistantText))
            {
                return new ContextRelevance
                {
                    IsRelevant = false,
                    ConfidenceScore = 0.3,
                    Reason = "Respuesta vacía del assistant",
                    IsExplicitContextSwitch = false
                };
            }

            try
            {
                // El prompt debería devolver JSON estructurado como:
                // {"relevant": true/false, "confidence": 0.0-1.0, "reason": "...", "context_switch": true/false}
                var result = System.Text.Json.JsonSerializer.Deserialize<ContextAnalysisResult>(assistantText, 
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return new ContextRelevance
                {
                    IsRelevant = result?.Relevant ?? true,
                    ConfidenceScore = result?.Confidence ?? 0.5,
                    Reason = result?.Reason ?? "Análisis LLM",
                    IsExplicitContextSwitch = result?.ContextSwitch ?? false
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing context analysis result: {Text}", assistantText);
                
                // Fallback heurístico
                return new ContextRelevance
                {
                    IsRelevant = true,
                    ConfidenceScore = 0.6,
                    Reason = "Fallback - error parsing assistant response",
                    IsExplicitContextSwitch = false
                };
            }
        }

        /// <summary>
        /// Obtiene la API key de OpenAI (temporal, debería venir de configuración)
        /// </summary>
        private string GetOpenAIApiKey()
        {
            return Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "dummy";
        }

        private ContextRelevance AnalyzeRelevanceWithHeuristics(string message, ConversationContext context)
        {
            var normalizedMessage = message.Trim().ToLowerInvariant();
            
            // Lista de palabras claramente irrelevantes
            var irrelevantWords = new[] 
            { 
                "perro", "gato", "auto", "casa", "verde", "azul", "rojo", "mesa", "silla",
                "comida", "agua", "libro", "película", "música", "fútbol", "parque", "playa"
            };

            if (irrelevantWords.Any(word => normalizedMessage.Contains(word)))
            {
                return new ContextRelevance
                {
                    IsRelevant = false,
                    ConfidenceScore = 0.8,
                    Reason = "Contiene palabras claramente irrelevantes al contexto médico"
                };
            }

            // Si es muy corto y no contiene información útil
            if (normalizedMessage.Length < 3)
            {
                return new ContextRelevance
                {
                    IsRelevant = false,
                    ConfidenceScore = 0.7,
                    Reason = "Mensaje demasiado corto para ser relevante"
                };
            }

            // Por defecto, asumir relevancia
            return new ContextRelevance
            {
                IsRelevant = true,
                ConfidenceScore = 0.6,
                Reason = "Análisis heurístico - asumiendo relevancia"
            };
        }

        private (bool IsExplicitSwitch, string Reason) DetectExplicitContextSwitch(string message)
        {
            var normalizedMessage = message.Trim().ToLowerInvariant();
            
            Console.WriteLine($"[CONTEXT-SWITCH] Analyzing message: '{normalizedMessage}'");
            
            foreach (var keyword in _contextSwitchKeywords)
            {
                if (normalizedMessage.Contains(keyword))
                {
                    Console.WriteLine($"[CONTEXT-SWITCH] ✅ Found keyword: '{keyword}' in message");
                    return (true, $"Palabra clave de cambio de contexto detectada: '{keyword}'");
                }
            }

            Console.WriteLine($"[CONTEXT-SWITCH] ❌ No context switch keywords found");
            return (false, "");
        }

        private string GetProgressDetails(ConversationContext context)
        {
            if (context.MessageCount > 1)
            {
                var timeElapsed = DateTime.UtcNow - context.StartedAt;
                if (timeElapsed.TotalMinutes > 2)
                {
                    return $" (venimos charlando {timeElapsed.TotalMinutes:F0} min)";
                }
                else if (context.MessageCount > 2)
                {
                    return $" (ya van {context.MessageCount} mensajes)";
                }
            }
            return "";
        }

        private string GetFieldHumanName(string fieldName)
        {
            return fieldName?.ToLowerInvariant() switch
            {
                "fechahora" => "fecha y hora",
                "lugar" => "lugar/hospital",
                "cirujano" => "cirujano",
                "anestesiologo" => "anestesiólogo",
                "cirugia" => "tipo de cirugía",
                "cantidad" => "cantidad de cirugías",
                _ => fieldName ?? "información"
            };
        }

        private bool HasSomeData(Appointment appointment)
        {
            return appointment.FechaHora != null ||
                   !string.IsNullOrEmpty(appointment.Lugar) ||
                   !string.IsNullOrEmpty(appointment.Cirujano) ||
                   !string.IsNullOrEmpty(appointment.Cirugia) ||
                   appointment.Cantidad != null ||
                   !string.IsNullOrEmpty(appointment.Anestesiologo);
        }

        /// <summary>
        /// Detecta si un mensaje contiene datos de nueva cirugía usando LLM especializado
        /// </summary>
        private async Task<NewSurgeryDetectionResult> DetectNewSurgeryWithLLM(string message, ConversationContext context, CancellationToken ct)
        {
            try
            {
                // Crear input para el detector de nueva cirugía
                var detectorInput = BuildNewSurgeryDetectorInput(message, context);
                
                // Llamar al prompt especializado
                var response = await CallNewSurgeryDetectorPrompt(detectorInput, ct);
                
                // Parsear respuesta
                return ParseNewSurgeryDetectorResponse(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in new surgery detection, falling back to heuristics");
                
                // Fallback: análisis simple
                return new NewSurgeryDetectionResult
                {
                    IsNewSurgery = false,
                    Confidence = 0.5,
                    Reason = "Error en detección LLM - usando fallback",
                    ElementsDetected = new()
                };
            }
        }

        /// <summary>
        /// Construye el input para el detector de nueva cirugía
        /// </summary>
        private string BuildNewSurgeryDetectorInput(string message, ConversationContext context)
        {
            var contextType = context.Type switch
            {
                ContextType.Confirming => "esperando_confirmacion",
                ContextType.FieldWizard when context.CurrentField?.Contains("fecha") == true => "esperando_campo_fecha",
                ContextType.FieldWizard when context.CurrentField?.Contains("lugar") == true => "esperando_campo_lugar", 
                ContextType.FieldWizard when context.CurrentField?.Contains("cirujano") == true => "esperando_campo_cirujano",
                ContextType.FieldWizard when context.CurrentField?.Contains("anestesiologo") == true => "esperando_campo_anestesiologo",
                ContextType.ModifyingSurgery => "modificando_cirugia",
                ContextType.RegisteringSurgery => "registrando_cirugia",
                _ => "conversacion_activa"
            };

            // Crear input estructurado JSON para el prompt
            return $@"{{
                ""contexto_actual"": ""{contextType}"",
                ""detalles_contexto"": ""{context.Details?.Replace("\"", "\\\"") ?? ""}"",
                ""mensaje_usuario"": ""{message.Replace("\"", "\\\"")}"" 
            }}";
        }

        /// <summary>
        /// Llama al prompt detector de nueva cirugía
        /// </summary>
        private async Task<string> CallNewSurgeryDetectorPrompt(string inputText, CancellationToken ct)
        {
            var body = new
            {
                prompt = new { id = NewSurgeryDetectorPromptId, version = NewSurgeryDetectorPromptVersion },
                input = inputText
            };

            var jsonBody = System.Text.Json.JsonSerializer.Serialize(body);
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GetOpenAIApiKey());
            
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("https://api.openai.com/v1/responses", content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Error calling new surgery detector prompt: {response.StatusCode} - {errorContent}");
            }
            
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>
        /// Parsea la respuesta del detector de nueva cirugía
        /// </summary>
        private NewSurgeryDetectionResult ParseNewSurgeryDetectorResponse(string response)
        {
            try
            {
                // Parsear la respuesta del prompt
                var jsonDoc = System.Text.Json.JsonDocument.Parse(response);
                
                // Buscar en 'output' el message con la respuesta
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
                                    var assistantText = textProperty.GetString();
                                    return ParseNewSurgeryJson(assistantText);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing new surgery detector response: {Response}", response);
            }

            // Fallback
            return new NewSurgeryDetectionResult
            {
                IsNewSurgery = false,
                Confidence = 0.3,
                Reason = "Error parseando respuesta del detector",
                ElementsDetected = new()
            };
        }

        /// <summary>
        /// Parsea el JSON específico del detector de nueva cirugía
        /// </summary>
        private NewSurgeryDetectionResult ParseNewSurgeryJson(string? jsonText)
        {
            if (string.IsNullOrEmpty(jsonText))
            {
                return new NewSurgeryDetectionResult
                {
                    IsNewSurgery = false,
                    Confidence = 0.3,
                    Reason = "Respuesta vacía del detector",
                    ElementsDetected = new()
                };
            }

            try
            {
                // El prompt devuelve JSON como:
                // {"es_nueva_cirugia": true, "confianza": 0.9, "razon": "...", "elementos_detectados": [...]}
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = jsonDoc.RootElement;

                var esNuevaCirugia = root.TryGetProperty("es_nueva_cirugia", out var esNuevaProperty) ? 
                    esNuevaProperty.GetBoolean() : false;

                var confianza = root.TryGetProperty("confianza", out var confianzaProperty) ? 
                    confianzaProperty.GetDouble() : 0.5;

                var razon = root.TryGetProperty("razon", out var razonProperty) ? 
                    razonProperty.GetString() ?? "" : "";

                var elementos = new List<string>();
                if (root.TryGetProperty("elementos_detectados", out var elementosProperty) && 
                    elementosProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var elemento in elementosProperty.EnumerateArray())
                    {
                        if (elemento.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            elementos.Add(elemento.GetString() ?? "");
                        }
                    }
                }

                return new NewSurgeryDetectionResult
                {
                    IsNewSurgery = esNuevaCirugia,
                    Confidence = confianza,
                    Reason = razon,
                    ElementsDetected = elementos
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing new surgery JSON: {Text}", jsonText);
                
                return new NewSurgeryDetectionResult
                {
                    IsNewSurgery = false,
                    Confidence = 0.4,
                    Reason = "Error parsing JSON del detector",
                    ElementsDetected = new()
                };
            }
        }
    }

    /// <summary>
    /// Clase auxiliar para deserialización del resultado de análisis de contexto
    /// </summary>
    public class ContextAnalysisResult
    {
        public bool Relevant { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
        public bool ContextSwitch { get; set; }
    }

    /// <summary>
    /// Resultado del detector especializado de nueva cirugía
    /// </summary>
    public class NewSurgeryDetectionResult
    {
        public bool IsNewSurgery { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; } = "";
        public List<string> ElementsDetected { get; set; } = new();
    }
}