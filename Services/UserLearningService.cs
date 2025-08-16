using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RegistroCx.Domain;
using RegistroCx.Services.Repositories;

namespace RegistroCx.Services;

public class UserLearningService
{
    private readonly IUserLearningRepository _learningRepo;
    private readonly ILogger<UserLearningService> _logger;

    public UserLearningService(IUserLearningRepository learningRepo, ILogger<UserLearningService> logger)
    {
        _learningRepo = learningRepo;
        _logger = logger;
    }

    /// <summary>
    /// Aprende de una interacción exitosa del usuario
    /// </summary>
    public async Task LearnFromInteraction(long chatId, string userInput, Dictionary<string, string> extractedData, CancellationToken ct = default)
    {
        _logger.LogInformation("[USER-LEARNING] Learning from interaction for user {ChatId}: {UserInput}", chatId, userInput);

        try
        {
            // Aprender términos de cirugías
            if (extractedData.TryGetValue("cirugia", out var surgeryExtracted) && !string.IsNullOrEmpty(surgeryExtracted))
            {
                await LearnSurgeryTerms(chatId, userInput, surgeryExtracted, ct);
            }

            // Aprender cirujanos
            if (extractedData.TryGetValue("cirujano", out var surgeonExtracted) && !string.IsNullOrEmpty(surgeonExtracted))
            {
                await LearnSurgeonTerms(chatId, userInput, surgeonExtracted, ct);
            }

            // Aprender lugares
            if (extractedData.TryGetValue("lugar", out var placeExtracted) && !string.IsNullOrEmpty(placeExtracted))
            {
                await LearnPlaceTerms(chatId, userInput, placeExtracted, ct);
            }

            // Aprender anestesiólogos
            if (extractedData.TryGetValue("anestesiologo", out var anesthExtracted) && !string.IsNullOrEmpty(anesthExtracted))
            {
                await LearnAnesthesiologistTerms(chatId, userInput, anesthExtracted, ct);
            }

            // Aprender patrones de comunicación
            await LearnCommunicationPatterns(chatId, extractedData, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[USER-LEARNING] Error learning from interaction for user {ChatId}", chatId);
        }
    }

    /// <summary>
    /// Construye contexto personalizado para incluir en el prompt
    /// </summary>
    public async Task<string> BuildPersonalizedPromptContext(long chatId, CancellationToken ct = default)
    {
        try
        {
            var surgeryTerms = await _learningRepo.GetUserCustomTermsByTypeAsync(chatId, TermTypes.Surgery, ct);
            var surgeonTerms = await _learningRepo.GetUserCustomTermsByTypeAsync(chatId, TermTypes.Surgeon, ct);
            var placeTerms = await _learningRepo.GetUserCustomTermsByTypeAsync(chatId, TermTypes.Place, ct);
            var patterns = await _learningRepo.GetUserPatternsAsync(chatId, ct);

            if (!surgeryTerms.Any() && !surgeonTerms.Any() && !placeTerms.Any() && !patterns.Any())
            {
                return string.Empty; // No hay contexto personalizado aún
            }

            var context = new StringBuilder();
            context.AppendLine("=== CONTEXTO PERSONALIZADO USUARIO ===");

            // Términos de cirugías personalizados
            if (surgeryTerms.Any())
            {
                context.AppendLine("TÉRMINOS DE CIRUGÍAS PERSONALIZADOS:");
                foreach (var term in surgeryTerms.Where(t => t.Confidence >= 0.6m).Take(10))
                {
                    context.AppendLine($"• \"{term.UserTerm}\" = \"{term.StandardTerm}\" (usado {term.Frequency} veces, confianza: {term.Confidence:F2})");
                }
                context.AppendLine();
            }

            // Cirujanos frecuentes
            if (surgeonTerms.Any())
            {
                context.AppendLine("CIRUJANOS FRECUENTES:");
                foreach (var term in surgeonTerms.Where(t => t.Confidence >= 0.6m).Take(5))
                {
                    context.AppendLine($"• \"{term.UserTerm}\" = \"{term.StandardTerm}\" (usado {term.Frequency} veces)");
                }
                context.AppendLine();
            }

            // Lugares frecuentes
            if (placeTerms.Any())
            {
                context.AppendLine("LUGARES FRECUENTES:");
                foreach (var term in placeTerms.Where(t => t.Confidence >= 0.6m).Take(5))
                {
                    context.AppendLine($"• \"{term.UserTerm}\" = \"{term.StandardTerm}\" (usado {term.Frequency} veces)");
                }
                context.AppendLine();
            }

            // Patrones de uso
            var frequentSurgeries = patterns.Where(p => p.PatternType == PatternTypes.FrequentSurgery)
                                           .OrderByDescending(p => p.Frequency)
                                           .Take(5);
            if (frequentSurgeries.Any())
            {
                context.AppendLine("CIRUGÍAS MÁS FRECUENTES:");
                context.AppendLine(string.Join(", ", frequentSurgeries.Select(p => $"{p.PatternValue} ({p.Frequency}x)")));
                context.AppendLine();
            }

            context.AppendLine("INSTRUCCIONES PERSONALIZADAS:");
            context.AppendLine("- Prioriza los términos aprendidos de este usuario sobre coincidencias genéricas");
            context.AppendLine("- Si el usuario usa un término conocido, aplica la traducción directamente");
            context.AppendLine("- Si hay ambigüedad, sugiere el término más usado por este usuario");
            context.AppendLine("=== FIN CONTEXTO PERSONALIZADO ===");

            _logger.LogInformation("[USER-LEARNING] Built personalized context for user {ChatId}: {TermCount} terms, {PatternCount} patterns", 
                chatId, surgeryTerms.Count + surgeonTerms.Count + placeTerms.Count, patterns.Count);

            return context.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[USER-LEARNING] Error building personalized context for user {ChatId}", chatId);
            return string.Empty;
        }
    }

    /// <summary>
    /// Obtiene sugerencias inteligentes basadas en el historial del usuario
    /// </summary>
    public async Task<List<string>> GetSmartSuggestions(long chatId, string termType, string partialInput, CancellationToken ct = default)
    {
        var suggestions = new List<string>();

        try
        {
            var userTerms = await _learningRepo.GetUserCustomTermsByTypeAsync(chatId, termType, ct);
            
            // Buscar coincidencias parciales en términos del usuario
            var matches = userTerms.Where(t => 
                t.UserTerm.Contains(partialInput.ToLowerInvariant()) ||
                t.StandardTerm.ToLowerInvariant().Contains(partialInput.ToLowerInvariant()))
                .OrderByDescending(t => t.Frequency)
                .Take(3);

            suggestions.AddRange(matches.Select(m => $"{m.StandardTerm} (como siempre usás \"{m.UserTerm}\")"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[USER-LEARNING] Error getting suggestions for user {ChatId}", chatId);
        }

        return suggestions;
    }

    private async Task LearnSurgeryTerms(long chatId, string userInput, string extractedSurgery, CancellationToken ct)
    {
        // Buscar qué palabra del input se mapeó a la cirugía extraída
        var inputWords = ExtractPotentialSurgeryTerms(userInput);
        
        foreach (var word in inputWords)
        {
            // Solo aprender si la palabra no es exactamente igual al término estándar
            if (!string.Equals(word, extractedSurgery, StringComparison.OrdinalIgnoreCase))
            {
                var existingTerm = await _learningRepo.GetUserCustomTermAsync(chatId, word, TermTypes.Surgery, ct);
                
                if (existingTerm != null)
                {
                    // Incrementar frecuencia del término existente
                    await _learningRepo.UpdateTermFrequencyAsync(chatId, word, TermTypes.Surgery, ct);
                }
                else
                {
                    // Crear nuevo término personalizado
                    var newTerm = new UserCustomTerm
                    {
                        ChatId = chatId,
                        UserTerm = word,
                        StandardTerm = extractedSurgery,
                        TermType = TermTypes.Surgery,
                        Frequency = 1,
                        Confidence = 0.7m, // Confianza inicial moderada
                        FirstSeen = DateTime.UtcNow,
                        LastUsed = DateTime.UtcNow
                    };

                    await _learningRepo.SaveUserCustomTermAsync(newTerm, ct);
                }

                _logger.LogInformation("[USER-LEARNING] Learned surgery term: '{UserTerm}' -> '{StandardTerm}' for user {ChatId}", 
                    word, extractedSurgery, chatId);
            }
        }

        // Guardar patrón de cirugía frecuente
        await SaveFrequentPattern(chatId, PatternTypes.FrequentSurgery, extractedSurgery, ct);
    }

    private async Task LearnSurgeonTerms(long chatId, string userInput, string extractedSurgeon, CancellationToken ct)
    {
        var inputWords = ExtractPotentialPersonNames(userInput);
        
        foreach (var word in inputWords)
        {
            if (!string.Equals(word, extractedSurgeon, StringComparison.OrdinalIgnoreCase))
            {
                var existingTerm = await _learningRepo.GetUserCustomTermAsync(chatId, word, TermTypes.Surgeon, ct);
                
                if (existingTerm != null)
                {
                    await _learningRepo.UpdateTermFrequencyAsync(chatId, word, TermTypes.Surgeon, ct);
                }
                else
                {
                    var newTerm = new UserCustomTerm
                    {
                        ChatId = chatId,
                        UserTerm = word,
                        StandardTerm = extractedSurgeon,
                        TermType = TermTypes.Surgeon,
                        Frequency = 1,
                        Confidence = 0.8m, // Nombres suelen ser más específicos
                        FirstSeen = DateTime.UtcNow,
                        LastUsed = DateTime.UtcNow
                    };

                    await _learningRepo.SaveUserCustomTermAsync(newTerm, ct);
                }

                _logger.LogInformation("[USER-LEARNING] Learned surgeon term: '{UserTerm}' -> '{StandardTerm}' for user {ChatId}", 
                    word, extractedSurgeon, chatId);
            }
        }

        await SaveFrequentPattern(chatId, PatternTypes.TypicalSurgeon, extractedSurgeon, ct);
    }

    private async Task LearnPlaceTerms(long chatId, string userInput, string extractedPlace, CancellationToken ct)
    {
        var inputWords = ExtractPotentialPlaceNames(userInput);
        
        foreach (var word in inputWords)
        {
            if (!string.Equals(word, extractedPlace, StringComparison.OrdinalIgnoreCase))
            {
                var existingTerm = await _learningRepo.GetUserCustomTermAsync(chatId, word, TermTypes.Place, ct);
                
                if (existingTerm != null)
                {
                    await _learningRepo.UpdateTermFrequencyAsync(chatId, word, TermTypes.Place, ct);
                }
                else
                {
                    var newTerm = new UserCustomTerm
                    {
                        ChatId = chatId,
                        UserTerm = word,
                        StandardTerm = extractedPlace,
                        TermType = TermTypes.Place,
                        Frequency = 1,
                        Confidence = 0.8m,
                        FirstSeen = DateTime.UtcNow,
                        LastUsed = DateTime.UtcNow
                    };

                    await _learningRepo.SaveUserCustomTermAsync(newTerm, ct);
                }

                _logger.LogInformation("[USER-LEARNING] Learned place term: '{UserTerm}' -> '{StandardTerm}' for user {ChatId}", 
                    word, extractedPlace, chatId);
            }
        }

        await SaveFrequentPattern(chatId, PatternTypes.PreferredPlace, extractedPlace, ct);
    }

    private async Task LearnAnesthesiologistTerms(long chatId, string userInput, string extractedAnesthesiologist, CancellationToken ct)
    {
        var inputWords = ExtractPotentialPersonNames(userInput);
        
        foreach (var word in inputWords)
        {
            if (!string.Equals(word, extractedAnesthesiologist, StringComparison.OrdinalIgnoreCase))
            {
                var existingTerm = await _learningRepo.GetUserCustomTermAsync(chatId, word, TermTypes.Anesthesiologist, ct);
                
                if (existingTerm != null)
                {
                    await _learningRepo.UpdateTermFrequencyAsync(chatId, word, TermTypes.Anesthesiologist, ct);
                }
                else
                {
                    var newTerm = new UserCustomTerm
                    {
                        ChatId = chatId,
                        UserTerm = word,
                        StandardTerm = extractedAnesthesiologist,
                        TermType = TermTypes.Anesthesiologist,
                        Frequency = 1,
                        Confidence = 0.8m,
                        FirstSeen = DateTime.UtcNow,
                        LastUsed = DateTime.UtcNow
                    };

                    await _learningRepo.SaveUserCustomTermAsync(newTerm, ct);
                }
            }
        }
    }

    private async Task LearnCommunicationPatterns(long chatId, Dictionary<string, string> extractedData, CancellationToken ct)
    {
        // Aprender cantidad típica si está presente
        if (extractedData.TryGetValue("cantidad", out var quantity) && !string.IsNullOrEmpty(quantity))
        {
            await SaveFrequentPattern(chatId, PatternTypes.UsualQuantity, quantity, ct);
        }
    }

    private async Task SaveFrequentPattern(long chatId, string patternType, string patternValue, CancellationToken ct)
    {
        var existingPattern = await _learningRepo.GetUserPatternAsync(chatId, patternType, ct);
        
        if (existingPattern != null && existingPattern.PatternValue == patternValue)
        {
            await _learningRepo.UpdatePatternFrequencyAsync(chatId, patternType, patternValue, ct);
        }
        else
        {
            var newPattern = new UserCommunicationPattern
            {
                ChatId = chatId,
                PatternType = patternType,
                PatternValue = patternValue,
                Frequency = 1,
                Confidence = 0.6m,
                LastUsed = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _learningRepo.SaveUserPatternAsync(newPattern, ct);
        }
    }

    // Métodos helper para extraer términos potenciales del input
    private List<string> ExtractPotentialSurgeryTerms(string input)
    {
        var terms = new List<string>();
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Buscar palabras que podrían ser cirugías (generalmente sustantivos médicos)
        var surgeryPatterns = new[]
        {
            @"\b(cataratas?|faco|facoemulsificacion|cers?|hava|adenoides?|amígdalas?|mld)\b",
            @"\b\w+ectomía\b", @"\b\w+plastia\b", @"\b\w+tomía\b", @"\b\w+scopía\b"
        };

        foreach (var pattern in surgeryPatterns)
        {
            var matches = Regex.Matches(input.ToLowerInvariant(), pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                terms.Add(match.Value.Trim());
            }
        }

        return terms.Distinct().ToList();
    }

    private List<string> ExtractPotentialPersonNames(string input)
    {
        var names = new List<string>();
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Buscar palabras que empiecen con mayúscula (posibles nombres)
        foreach (var word in words)
        {
            var cleanWord = Regex.Replace(word, @"[^\w]", "");
            if (cleanWord.Length >= 3 && char.IsUpper(cleanWord[0]))
            {
                names.Add(cleanWord.ToLowerInvariant());
            }
        }

        return names.Distinct().ToList();
    }

    private List<string> ExtractPotentialPlaceNames(string input)
    {
        var places = new List<string>();
        
        // Buscar patrones de lugares comunes
        var placePatterns = new[]
        {
            @"\b(sanatorio|hospital|clínica|centro|instituto)\s+\w+\b",
            @"\b\w+(callao?|callo|anchorena|mater|dei|italiano|alemán)\b",
            @"\b(callo|callao|ancho|anchorena)\b"
        };

        foreach (var pattern in placePatterns)
        {
            var matches = Regex.Matches(input.ToLowerInvariant(), pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                places.Add(match.Value.Trim());
            }
        }

        return places.Distinct().ToList();
    }
}