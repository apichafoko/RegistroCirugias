using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RegistroCx.Services;

public class AnesthesiologistSearchService : IAnesthesiologistSearchService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Usar el mismo prompt ID que el existente
    private const string PromptId = "pmpt_688fff5af7e48190bdae049dcfdc44a5038f25fca90d0503";
    private const string PromptVersion = "6";

    public AnesthesiologistSearchService(string apiKey)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com")
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<List<AnesthesiologistCandidate>> SearchByPartialNameAsync(string partialName, string teamEmail)
    {
        try
        {
            var searchRequest = new
            {
                operacion = "buscar_anestesiologo_por_similitud",
                nombre_parcial = partialName.ToLowerInvariant().Trim(),
                equipo_email = teamEmail
            };

            var requestJson = JsonSerializer.Serialize(searchRequest, _jsonOptions);
            
            var body = new
            {
                prompt = new { id = PromptId, version = PromptVersion },
                input = requestJson
            };

            var jsonBody = JsonSerializer.Serialize(body, _jsonOptions);
            Console.WriteLine($"[ANESTHESIOLOGIST_SEARCH] Request: {jsonBody}");

            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("/v1/responses", content);

            if (!resp.IsSuccessStatusCode)
            {
                var errorContent = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"[ANESTHESIOLOGIST_SEARCH] Error {resp.StatusCode}: {errorContent}");
                return new List<AnesthesiologistCandidate>();
            }

            var raw = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"[ANESTHESIOLOGIST_SEARCH] Response: {raw}");

            // Extraer el texto del assistant
            var assistantText = ExtractAssistantText(raw);
            if (string.IsNullOrEmpty(assistantText))
            {
                Console.WriteLine("[ANESTHESIOLOGIST_SEARCH] No assistant text found");
                return new List<AnesthesiologistCandidate>();
            }

            // Parsear la respuesta JSON del LLM
            var searchResponse = JsonSerializer.Deserialize<AnesthesiologistSearchResponse>(assistantText, _jsonOptions);
            
            var candidates = new List<AnesthesiologistCandidate>();
            if (searchResponse?.Candidatos != null)
            {
                foreach (var candidate in searchResponse.Candidatos)
                {
                    candidates.Add(new AnesthesiologistCandidate
                    {
                        Nombre = candidate.Nombre ?? "",
                        Email = candidate.Email ?? "",
                        Coincidencia = candidate.Coincidencia ?? "parcial"
                    });
                }
            }

            Console.WriteLine($"[ANESTHESIOLOGIST_SEARCH] Found {candidates.Count} candidates");
            return candidates;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ANESTHESIOLOGIST_SEARCH] Error: {ex.Message}");
            return new List<AnesthesiologistCandidate>();
        }
    }

    private string ExtractAssistantText(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            
            if (!doc.RootElement.TryGetProperty("output", out var outputArr))
                return "";

            foreach (var outEntry in outputArr.EnumerateArray())
            {
                if (outEntry.GetProperty("type").GetString() == "message")
                {
                    foreach (var part in outEntry.GetProperty("content").EnumerateArray())
                    {
                        if (part.GetProperty("type").GetString() == "output_text")
                        {
                            return part.GetProperty("output_text").GetString() ?? "";
                        }
                    }
                }
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    private class AnesthesiologistSearchResponse
    {
        public List<CandidateDto>? Candidatos { get; set; }
    }

    private class CandidateDto
    {
        public string? Nombre { get; set; }
        public string? Email { get; set; }
        public string? Coincidencia { get; set; }
    }
}