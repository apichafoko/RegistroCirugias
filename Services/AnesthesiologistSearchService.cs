using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using RegistroCx.Services.Extraction;

namespace RegistroCx.Services;

public class AnesthesiologistSearchService : IAnesthesiologistSearchService
{
    private readonly LLMOpenAIAssistant _llm;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AnesthesiologistSearchService(LLMOpenAIAssistant llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public async Task<List<AnesthesiologistCandidate>> SearchByPartialNameAsync(string partialName, string teamEmail)
    {
        try
        {
            // Usar el método centralizado del LLMOpenAIAssistant
            var assistantText = await _llm.SearchAnesthesiologistAsync(partialName, teamEmail);
            
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