using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RegistroCx.Services.Extraction;

namespace RegistroCx.Services;

public class MultiSurgeryParser
{
    private readonly ILogger<MultiSurgeryParser> _logger;
    private readonly LLMOpenAIAssistant _llm;

    private readonly UserLearningService? _learningService;

    public MultiSurgeryParser(ILogger<MultiSurgeryParser> logger, LLMOpenAIAssistant llm, UserLearningService? learningService = null)
    {
        _logger = logger;
        _llm = llm;
        _learningService = learningService;
    }

    /// <summary>
    /// Detecta si el input contiene múltiples cirugías usando LLM y las separa en inputs individuales
    /// </summary>
    public async Task<ParseResult> ParseInputAsync(string originalInput, DateTime referenceDate, object? listasObj = null, long chatId = 0)
    {
        try
        {
            _logger.LogInformation("[MULTI-PARSER-LLM] Analyzing input: {Input}", originalInput);

            var detectionResult = await DetectMultipleSurgeriesWithLLM(originalInput, referenceDate, listasObj, chatId);
            
            // Si hay problemas de validación que requieren clarificación, retornar inmediatamente
            if (detectionResult.NeedsClarification || detectionResult.ValidationStatus == "error")
            {
                _logger.LogInformation("[MULTI-PARSER-LLM] Validation issues found, needs clarification");
                return new ParseResult
                {
                    IsMultiple = false,
                    OriginalInput = originalInput,
                    IndividualInputs = new List<string> { originalInput },
                    ValidationStatus = detectionResult.ValidationStatus,
                    Issues = detectionResult.Issues,
                    SuggestedResponse = detectionResult.SuggestedResponse,
                    NeedsClarification = detectionResult.NeedsClarification
                };
            }

            if (!detectionResult.IsMultiple || detectionResult.Surgeries.Count <= 1)
            {
                _logger.LogInformation("[MULTI-PARSER-LLM] Single surgery detected, no parsing needed");
                return new ParseResult
                {
                    IsMultiple = false,
                    OriginalInput = originalInput,
                    IndividualInputs = new List<string> { originalInput },
                    ValidationStatus = detectionResult.ValidationStatus,
                    Issues = detectionResult.Issues,
                    SuggestedResponse = detectionResult.SuggestedResponse,
                    NeedsClarification = detectionResult.NeedsClarification
                };
            }

            _logger.LogInformation("[MULTI-PARSER-LLM] ✅ {Count} surgeries detected", detectionResult.Surgeries.Count);

            var baseContext = ExtractBaseContextFromInput(originalInput, detectionResult.Surgeries);
            var individualInputs = BuildIndividualInputs(detectionResult.Surgeries, baseContext);

            return new ParseResult
            {
                IsMultiple = true,
                OriginalInput = originalInput,
                IndividualInputs = individualInputs,
                DetectedSurgeries = detectionResult.Surgeries,
                ValidationStatus = detectionResult.ValidationStatus,
                Issues = detectionResult.Issues,
                SuggestedResponse = detectionResult.SuggestedResponse,
                NeedsClarification = detectionResult.NeedsClarification
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MULTI-PARSER-LLM] Error parsing input, using fallback mock response");
            
            // FALLBACK: usar mock response cuando el LLM falla
            var mockResponse = BuildMockLLMResponse(originalInput);
            var mockResult = ParseLLMResponse(mockResponse);
            
            if (mockResult.IsMultiple)
            {
                _logger.LogInformation("[MULTI-PARSER-LLM] Mock detected multiple surgeries: {Count}", mockResult.Surgeries.Count);
                var baseContext = ExtractBaseContextFromInput(originalInput, mockResult.Surgeries);
                var individualInputs = BuildIndividualInputs(mockResult.Surgeries, baseContext);

                return new ParseResult
                {
                    IsMultiple = true,
                    OriginalInput = originalInput,
                    IndividualInputs = individualInputs,
                    DetectedSurgeries = mockResult.Surgeries
                };
            }
            
            return new ParseResult
            {
                IsMultiple = false,
                OriginalInput = originalInput,
                IndividualInputs = new List<string> { originalInput }
            };
        }
    }

    private async Task<LLMDetectionResult> DetectMultipleSurgeriesWithLLM(string input, DateTime referenceDate, object? listasObj, long chatId)
    {
        _logger.LogDebug("[MULTI-PARSER-LLM] Sending prompt to LLM with validation context");
        
        // Construir input con contexto personalizado si está disponible
        var enhancedInput = input;
        if (_learningService != null && chatId != 0)
        {
            try
            {
                var personalizedContext = await _learningService.BuildPersonalizedPromptContext(chatId);
                if (!string.IsNullOrEmpty(personalizedContext))
                {
                    enhancedInput = $"{personalizedContext}\n\n{input}";
                    _logger.LogDebug("[MULTI-PARSER-LLM] Enhanced input with personalized context for user {ChatId}", chatId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MULTI-PARSER-LLM] Failed to build personalized context for user {ChatId}, using original input", chatId);
            }
        }
        
        // Usar el prompt específico para detección de múltiples cirugías con validaciones
        var extractedData = await _llm.ExtractMultipleSurgeriesAsync(enhancedInput, referenceDate, listasObj);
        
        // Convertir el Dictionary a nuestro formato esperado
        var llmResponse = ConvertDictionaryToJson(extractedData);
        
        _logger.LogDebug("[MULTI-PARSER-LLM] LLM response: {Response}", llmResponse);
        
        return ParseLLMResponse(llmResponse);
    }
    
    
    private LLMDetectionResult ParseLLMResponse(string llmResponse)
    {
        try
        {
            // Limpiar respuesta y extraer JSON
            var cleanResponse = ExtractJsonFromResponse(llmResponse);
            
            var jsonDoc = JsonDocument.Parse(cleanResponse);
            var root = jsonDoc.RootElement;
            
            var isMultiple = root.GetProperty("multiple").GetBoolean();
            var surgeries = new List<SurgeryInfo>();
            
            if (root.TryGetProperty("surgeries", out var surgeriesArray))
            {
                foreach (var surgeryElement in surgeriesArray.EnumerateArray())
                {
                    var quantity = surgeryElement.GetProperty("quantity").GetInt32();
                    var name = surgeryElement.GetProperty("name").GetString() ?? "";
                    
                    surgeries.Add(new SurgeryInfo
                    {
                        Quantity = quantity,
                        SurgeryName = name,
                        OriginalText = $"{quantity} {name}"
                    });
                }
            }

            // Parsear nuevas propiedades de validación
            var validationStatus = root.TryGetProperty("validation_status", out var statusProp) 
                ? statusProp.GetString() ?? "valid" 
                : "valid";
            
            var issues = new List<ValidationIssue>();
            if (root.TryGetProperty("issues", out var issuesArray))
            {
                foreach (var issueElement in issuesArray.EnumerateArray())
                {
                    var type = issueElement.GetProperty("type").GetString() ?? "";
                    var message = issueElement.GetProperty("message").GetString() ?? "";
                    
                    issues.Add(new ValidationIssue { Type = type, Message = message });
                }
            }

            var suggestedResponse = root.TryGetProperty("suggested_response", out var responseProp)
                ? responseProp.GetString()
                : null;

            var needsClarification = root.TryGetProperty("needs_clarification", out var clarificationProp)
                ? clarificationProp.GetBoolean()
                : false;
            
            _logger.LogInformation("[MULTI-PARSER-LLM] Parsed {Count} surgeries, status: {Status}, issues: {IssueCount}", 
                surgeries.Count, validationStatus, issues.Count);
            
            return new LLMDetectionResult
            {
                IsMultiple = isMultiple,
                Surgeries = surgeries,
                ValidationStatus = validationStatus,
                Issues = issues,
                SuggestedResponse = suggestedResponse,
                NeedsClarification = needsClarification
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MULTI-PARSER-LLM] Error parsing LLM response: {Response}", llmResponse);
            return new LLMDetectionResult { IsMultiple = false, Surgeries = new List<SurgeryInfo>() };
        }
    }
    
    private string ExtractJsonFromResponse(string response)
    {
        // Buscar el primer JSON válido y descartar duplicados
        var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        var jsonBuilder = new StringBuilder();
        int braceCount = 0;
        bool insideJson = false;
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Buscar inicio de JSON
            if (!insideJson && trimmedLine.StartsWith('{'))
            {
                insideJson = true;
                jsonBuilder.Clear(); // Limpiar cualquier contenido previo
            }
            
            if (insideJson)
            {
                jsonBuilder.AppendLine(trimmedLine);
                
                // Contar llaves para saber cuándo termina el JSON
                foreach (char c in trimmedLine)
                {
                    if (c == '{') braceCount++;
                    else if (c == '}') braceCount--;
                }
                
                // Si las llaves están balanceadas, hemos terminado el primer JSON
                if (braceCount == 0)
                {
                    var firstJson = jsonBuilder.ToString().Trim();
                    _logger.LogDebug("[MULTI-PARSER-LLM] Extracted first JSON: {Json}", firstJson);
                    return firstJson;
                }
            }
        }
        
        // Fallback: usar el método original si no se encontró JSON válido
        var startIndex = response.IndexOf('{');
        var endIndex = response.IndexOf('}', startIndex);
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return response.Substring(startIndex, endIndex - startIndex + 1);
        }
        
        return response.Trim();
    }
    
    private string ConvertDictionaryToJson(Dictionary<string, string> extractedData)
    {
        // El LLM devuelve el JSON raw en la clave "raw_response"
        if (extractedData.TryGetValue("raw_response", out var rawResponse))
        {
            return rawResponse;
        }
        
        // Fallback en caso de que no haya respuesta válida
        _logger.LogWarning("[MULTI-PARSER-LLM] No raw_response found in extracted data");
        return @"{""multiple"": false, ""surgeries"": [{""quantity"": 1, ""name"": ""UNKNOWN""}]}";
    }
    
    private string BuildMockLLMResponse(string input)
    {
        // Mock response basada en patrones simples para testing
        // TODO: Reemplazar con llamada real al LLM cuando esté disponible
        
        var inputLower = input.ToLowerInvariant();
        
        // Detectar patrón "X cirugía y Y cirugía"
        var match = Regex.Match(inputLower, @"(\d+)\s+([a-záéíóúñ]+)\s+y\s+(\d+)\s+([a-záéíóúñ]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var qty1 = match.Groups[1].Value;
            var surgery1 = match.Groups[2].Value.ToUpperInvariant();
            var qty2 = match.Groups[3].Value;
            var surgery2 = match.Groups[4].Value.ToUpperInvariant();
            
            // Normalizar nombres conocidos
            surgery1 = NormalizeSurgeryName(surgery1);
            surgery2 = NormalizeSurgeryName(surgery2);
            
            return $@"{{
                ""multiple"": true,
                ""surgeries"": [
                    {{""quantity"": {qty1}, ""name"": ""{surgery1}""}},
                    {{""quantity"": {qty2}, ""name"": ""{surgery2}""}}
                ]
            }}";
        }
        
        // Si no encuentra múltiples, asumir una sola cirugía
        return $@"{{
            ""multiple"": false,
            ""surgeries"": [
                {{""quantity"": 1, ""name"": ""UNKNOWN""}}
            ]
        }}";
    }
    
    private string NormalizeSurgeryName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "cers" => "CERS",
            "hava" or "amigdalas" or "amígdalas" => "HAVA",
            "adenoides" => "ADENOIDES",
            "mld" => "MLD",
            _ => name.ToUpperInvariant()
        };
    }

    private string ExtractBaseContextFromInput(string originalInput, List<SurgeryInfo> surgeries)
    {
        var baseContext = originalInput;
        
        // Remover cada cirugía detectada del input original
        foreach (var surgery in surgeries)
        {
            // Crear patrones para remover variaciones de la cirugía
            var patterns = new[]
            {
                $@"\b{surgery.Quantity}\s+{Regex.Escape(surgery.SurgeryName)}\b",
                $@"\b{Regex.Escape(surgery.SurgeryName)}\s*x?\s*{surgery.Quantity}\b",
                $@"\b{surgery.Quantity}\s*x\s*{Regex.Escape(surgery.SurgeryName)}\b"
            };
            
            foreach (var pattern in patterns)
            {
                baseContext = System.Text.RegularExpressions.Regex.Replace(baseContext, pattern, "", RegexOptions.IgnoreCase);
            }
        }
        
        // Limpiar conectores comunes
        var connectorsToRemove = new[] { @"\s*\+\s*", @"\s*y\s*", @"\s*,\s*", @"\s*más\s*", @"\s*por\s*" };
        foreach (var connector in connectorsToRemove)
        {
            baseContext = System.Text.RegularExpressions.Regex.Replace(baseContext, connector, " ", RegexOptions.IgnoreCase);
        }
        
        // Limpiar espacios múltiples y trim
        baseContext = System.Text.RegularExpressions.Regex.Replace(baseContext, @"\s+", " ").Trim();
        
        _logger.LogDebug("[MULTI-PARSER-LLM] Base context extracted: {Context}", baseContext);
        return baseContext;
    }

    private List<string> BuildIndividualInputs(List<SurgeryInfo> surgeries, string baseContext)
    {
        var individualInputs = new List<string>();

        foreach (var surgery in surgeries)
        {
            // Construir input individual: "cantidad cirugía contexto_base"
            var individualInput = $"{surgery.Quantity} {surgery.SurgeryName} {baseContext}".Trim();
            individualInputs.Add(individualInput);
            
            _logger.LogInformation("[MULTI-PARSER] Individual input: {Input}", individualInput);
        }

        return individualInputs;
    }

    public class ParseResult
    {
        public bool IsMultiple { get; set; }
        public string OriginalInput { get; set; } = string.Empty;
        public List<string> IndividualInputs { get; set; } = new();
        public List<SurgeryInfo> DetectedSurgeries { get; set; } = new();
        public string ValidationStatus { get; set; } = "valid"; // valid|warning|error|question
        public List<ValidationIssue> Issues { get; set; } = new();
        public string? SuggestedResponse { get; set; }
        public bool NeedsClarification { get; set; }
    }

    public class SurgeryInfo
    {
        public int Quantity { get; set; }
        public string SurgeryName { get; set; } = string.Empty;
        public string OriginalText { get; set; } = string.Empty;
    }

    public class LLMDetectionResult
    {
        public bool IsMultiple { get; set; }
        public List<SurgeryInfo> Surgeries { get; set; } = new();
        public string ValidationStatus { get; set; } = "valid";
        public List<ValidationIssue> Issues { get; set; } = new();
        public string? SuggestedResponse { get; set; }
        public bool NeedsClarification { get; set; }
    }

    public class ValidationIssue
    {
        public string Type { get; set; } = string.Empty; // past_date, unknown_surgeon, invalid_time, etc.
        public string Message { get; set; } = string.Empty;
    }
}