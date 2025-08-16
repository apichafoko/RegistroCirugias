using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories;

public interface IUserLearningRepository
{
    // Métodos para términos personalizados
    Task<List<UserCustomTerm>> GetUserCustomTermsAsync(long chatId, CancellationToken ct = default);
    Task<List<UserCustomTerm>> GetUserCustomTermsByTypeAsync(long chatId, string termType, CancellationToken ct = default);
    Task<UserCustomTerm?> GetUserCustomTermAsync(long chatId, string userTerm, string termType, CancellationToken ct = default);
    Task<long> SaveUserCustomTermAsync(UserCustomTerm term, CancellationToken ct = default);
    Task UpdateTermFrequencyAsync(long chatId, string userTerm, string termType, CancellationToken ct = default);
    Task DeleteUserCustomTermAsync(long id, CancellationToken ct = default);

    // Métodos para patrones de comunicación
    Task<List<UserCommunicationPattern>> GetUserPatternsAsync(long chatId, CancellationToken ct = default);
    Task<UserCommunicationPattern?> GetUserPatternAsync(long chatId, string patternType, CancellationToken ct = default);
    Task<long> SaveUserPatternAsync(UserCommunicationPattern pattern, CancellationToken ct = default);
    Task UpdatePatternFrequencyAsync(long chatId, string patternType, string patternValue, CancellationToken ct = default);

    // Métodos de análisis
    Task<Dictionary<string, int>> GetMostUsedTermsByTypeAsync(long chatId, string termType, int limit = 10, CancellationToken ct = default);
    Task<List<UserCustomTerm>> GetHighConfidenceTermsAsync(long chatId, decimal minConfidence = 0.7m, CancellationToken ct = default);
}