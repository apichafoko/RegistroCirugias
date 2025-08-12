using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RegistroCx.Services.Extraction
{
    /// <summary>
    /// Cliente para llamar a tu Prompt publicado vía /v1/responses.
    /// </summary>
    public class LLMOpenAIAssistant
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Copia estos valores exactamente desde tu Dashboard → Deploy → Prompt ID / Version
        private const string PromptId      = "pmpt_688fff5af7e48190bdae049dcfdc44a5038f25fca90d0503";
        private const string PromptVersion = "4";
        
        // Prompt para detección de múltiples cirugías (actualizado para evitar duplicados)
        private const string MultiSurgeryPromptId      = "pmpt_689a0e6ad6988193a39feb176a30b80d0437b8506c01cf3d";
        private const string MultiSurgeryPromptVersion = "1";

        public LLMOpenAIAssistant(string apiKey)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com")
            };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        /// <summary>
        /// Envía el texto de usuario y la fecha al prompt publicado
        /// y devuelve el JSON de entidades parseado a Dictionary.
        /// </summary>
        public async Task<Dictionary<string, string>> ExtractWithPublishedPromptAsync(
            string userText,
            DateTime referenceDate)
        {
            // Construyo el input como un string
            var inputText = $"{userText}\n\nFECHA_HOY={referenceDate:dd/MM/yyyy}";
            var body = new
            {
                prompt = new { id = PromptId, version = PromptVersion },
                input  = inputText
            };

           // Serializar y depurar el JSON
            var jsonBody = JsonSerializer.Serialize(body, _jsonOptions);
            Console.WriteLine($"Request JSON: {jsonBody}");

            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            // Enviar la solicitud
            using var resp = await _http.PostAsync("/v1/responses", content);

            // Manejar errores
            if (!resp.IsSuccessStatusCode)
            {
                var errorContent = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Error {resp.StatusCode}: {errorContent}");
                throw new HttpRequestException($"Error {resp.StatusCode}: {errorContent}");
            }

            // Leer la respuesta
            var raw = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"Response JSON: {raw}");

            using var doc = JsonDocument.Parse(raw);

           // Buscar en 'output' el message
            if (!doc.RootElement.TryGetProperty("output", out var outputArr))
                throw new Exception("La respuesta no contiene 'output'.");

            string assistantText = null!;
            foreach (var outEntry in outputArr.EnumerateArray())
            {
                if (outEntry.GetProperty("type").GetString() == "message")
                {
                    foreach (var part in outEntry.GetProperty("content").EnumerateArray())
                    {
                        if (part.GetProperty("type").GetString() == "output_text")
                        {
                            assistantText = part.GetProperty("text").GetString()!;
                            break;
                        }
                    }
                    if (assistantText != null) break;
                }
            }
            if (assistantText == null)
                throw new Exception("No se encontró 'output_text' en la respuesta.");

            // Ahora parsear ese JSON puro a Dictionary<string,string>
            Console.WriteLine($"[LLM] Assistant text to parse: {assistantText.Trim()}");
            var dict = JSONExtractor.ParseLLMResponse(assistantText.Trim());

            Console.WriteLine($"[LLM] Parsed dictionary count: {dict?.Count ?? 0}");
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    Console.WriteLine($"[LLM] Key: '{kvp.Key}', Value: '{kvp.Value}'");
                }
            }

            if (dict == null)
                throw new Exception("Falló el parseo del JSON del assistant.");

            return dict;
        }
        
        /// <summary>
        /// Detecta múltiples cirugías usando el prompt específico para múltiples cirugías
        /// </summary>
        public async Task<Dictionary<string, string>> ExtractMultipleSurgeriesAsync(string userText)
        {
            var body = new
            {
                prompt = new { id = MultiSurgeryPromptId, version = MultiSurgeryPromptVersion },
                input  = userText
            };

           // Serializar y depurar el JSON
            var jsonBody = JsonSerializer.Serialize(body, _jsonOptions);
            Console.WriteLine($"[MULTI-SURGERY-LLM] Request JSON: {jsonBody}");

            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("/v1/responses", content);
            var responseText = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[MULTI-SURGERY-LLM] Full Response: {responseText}");
            
            // Debug: let's see the structure
            try
            {
                var debugDoc = JsonDocument.Parse(responseText);
                Console.WriteLine($"[MULTI-SURGERY-LLM] Root properties: {string.Join(", ", debugDoc.RootElement.EnumerateObject().Select(p => p.Name))}");
            }
            catch (Exception debugEx)
            {
                Console.WriteLine($"[MULTI-SURGERY-LLM] Debug parsing failed: {debugEx.Message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error from OpenAI API: {response.StatusCode} - {responseText}");
            }

            // Parsear la respuesta usando la misma estructura que el método original
            var jsonDoc = JsonDocument.Parse(responseText);
            string? assistantText = null;

            // Buscar en 'output' el message (igual que el método original)
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
                                assistantText = textProperty.GetString()!;
                                break;
                            }
                        }
                        if (assistantText != null) break;
                    }
                }
            }
            if (assistantText == null)
                throw new Exception("No se encontró 'output_text' en la respuesta del prompt de múltiples cirugías.");

            Console.WriteLine($"[MULTI-SURGERY-LLM] Assistant text to parse: {assistantText.Trim()}");
            
            // Para múltiples cirugías, necesitamos retornar el JSON raw, no parseado
            return new Dictionary<string, string> { ["raw_response"] = assistantText.Trim() };
        }
    }
}
