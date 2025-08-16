using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories;

public class UserLearningRepository : IUserLearningRepository
{
    private readonly ILogger<UserLearningRepository> _logger;
    private readonly string _connectionString;

    public UserLearningRepository(ILogger<UserLearningRepository> logger, string connectionString)
    {
        _logger = logger;
        _connectionString = connectionString;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<List<UserCustomTerm>> GetUserCustomTermsAsync(long chatId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, user_term, standard_term, term_type, frequency, confidence,
                   first_seen, last_used, created_at, updated_at
            FROM user_custom_terms 
            WHERE chat_id = @ChatId 
            ORDER BY frequency DESC, confidence DESC";

        var result = await connection.QueryAsync<UserCustomTerm>(sql, new { ChatId = chatId });
        return result.ToList();
    }

    public async Task<List<UserCustomTerm>> GetUserCustomTermsByTypeAsync(long chatId, string termType, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, user_term, standard_term, term_type, frequency, confidence,
                   first_seen, last_used, created_at, updated_at
            FROM user_custom_terms 
            WHERE chat_id = @ChatId AND term_type = @TermType
            ORDER BY frequency DESC, confidence DESC";

        var result = await connection.QueryAsync<UserCustomTerm>(sql, new { ChatId = chatId, TermType = termType });
        return result.ToList();
    }

    public async Task<UserCustomTerm?> GetUserCustomTermAsync(long chatId, string userTerm, string termType, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, user_term, standard_term, term_type, frequency, confidence,
                   first_seen, last_used, created_at, updated_at
            FROM user_custom_terms 
            WHERE chat_id = @ChatId AND user_term = @UserTerm AND term_type = @TermType";

        var result = await connection.QueryFirstOrDefaultAsync<UserCustomTerm>(
            sql, new { ChatId = chatId, UserTerm = userTerm.ToLowerInvariant(), TermType = termType });
        
        return result;
    }

    public async Task<long> SaveUserCustomTermAsync(UserCustomTerm term, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            INSERT INTO user_custom_terms (chat_id, user_term, standard_term, term_type, frequency, confidence, first_seen, last_used, created_at, updated_at)
            VALUES (@ChatId, @UserTerm, @StandardTerm, @TermType, @Frequency, @Confidence, @FirstSeen, @LastUsed, @CreatedAt, @UpdatedAt)
            ON CONFLICT (chat_id, user_term, term_type) 
            DO UPDATE SET 
                frequency = user_custom_terms.frequency + 1,
                confidence = LEAST(1.0, user_custom_terms.confidence + 0.1),
                last_used = @LastUsed,
                updated_at = @UpdatedAt
            RETURNING id";

        // Normalizar user_term a minúsculas para consistencia
        term.UserTerm = term.UserTerm.ToLowerInvariant();
        term.UpdatedAt = DateTime.UtcNow;

        var id = await connection.QuerySingleAsync<long>(sql, term);
        
        _logger.LogInformation("[USER-LEARNING] Saved/Updated term: {UserTerm} -> {StandardTerm} (type: {TermType}) for user {ChatId}", 
            term.UserTerm, term.StandardTerm, term.TermType, term.ChatId);
        
        return id;
    }

    public async Task UpdateTermFrequencyAsync(long chatId, string userTerm, string termType, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            UPDATE user_custom_terms 
            SET frequency = frequency + 1, 
                confidence = LEAST(1.0, confidence + 0.05),
                last_used = @LastUsed,
                updated_at = @UpdatedAt
            WHERE chat_id = @ChatId AND user_term = @UserTerm AND term_type = @TermType";

        var rowsAffected = await connection.ExecuteAsync(sql, new 
        { 
            ChatId = chatId, 
            UserTerm = userTerm.ToLowerInvariant(), 
            TermType = termType,
            LastUsed = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("[USER-LEARNING] Updated frequency for term: {UserTerm} (type: {TermType}) for user {ChatId}, rows affected: {RowsAffected}", 
            userTerm, termType, chatId, rowsAffected);
    }

    public async Task DeleteUserCustomTermAsync(long id, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = "DELETE FROM user_custom_terms WHERE id = @Id";
        await connection.ExecuteAsync(sql, new { Id = id });
    }

    // Métodos para patrones de comunicación
    public async Task<List<UserCommunicationPattern>> GetUserPatternsAsync(long chatId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, pattern_type, pattern_value, frequency, confidence, last_used, created_at, updated_at
            FROM user_communication_patterns 
            WHERE chat_id = @ChatId 
            ORDER BY frequency DESC";

        var result = await connection.QueryAsync<UserCommunicationPattern>(sql, new { ChatId = chatId });
        return result.ToList();
    }

    public async Task<UserCommunicationPattern?> GetUserPatternAsync(long chatId, string patternType, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, pattern_type, pattern_value, frequency, confidence, last_used, created_at, updated_at
            FROM user_communication_patterns 
            WHERE chat_id = @ChatId AND pattern_type = @PatternType
            ORDER BY frequency DESC LIMIT 1";

        var result = await connection.QueryFirstOrDefaultAsync<UserCommunicationPattern>(
            sql, new { ChatId = chatId, PatternType = patternType });
        
        return result;
    }

    public async Task<long> SaveUserPatternAsync(UserCommunicationPattern pattern, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            INSERT INTO user_communication_patterns (chat_id, pattern_type, pattern_value, frequency, confidence, last_used, created_at, updated_at)
            VALUES (@ChatId, @PatternType, @PatternValue, @Frequency, @Confidence, @LastUsed, @CreatedAt, @UpdatedAt)
            ON CONFLICT (chat_id, pattern_type, pattern_value) 
            DO UPDATE SET 
                frequency = user_communication_patterns.frequency + 1,
                confidence = LEAST(1.0, user_communication_patterns.confidence + 0.1),
                last_used = @LastUsed,
                updated_at = @UpdatedAt
            RETURNING id";

        pattern.UpdatedAt = DateTime.UtcNow;
        return await connection.QuerySingleAsync<long>(sql, pattern);
    }

    public async Task UpdatePatternFrequencyAsync(long chatId, string patternType, string patternValue, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            UPDATE user_communication_patterns 
            SET frequency = frequency + 1,
                confidence = LEAST(1.0, confidence + 0.05),
                last_used = @LastUsed,
                updated_at = @UpdatedAt
            WHERE chat_id = @ChatId AND pattern_type = @PatternType AND pattern_value = @PatternValue";

        await connection.ExecuteAsync(sql, new 
        { 
            ChatId = chatId, 
            PatternType = patternType,
            PatternValue = patternValue,
            LastUsed = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task<Dictionary<string, int>> GetMostUsedTermsByTypeAsync(long chatId, string termType, int limit = 10, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT standard_term, frequency
            FROM user_custom_terms 
            WHERE chat_id = @ChatId AND term_type = @TermType
            ORDER BY frequency DESC, confidence DESC
            LIMIT @Limit";

        var result = await connection.QueryAsync<(string StandardTerm, int Frequency)>(
            sql, new { ChatId = chatId, TermType = termType, Limit = limit });
        
        return result.ToDictionary(x => x.StandardTerm, x => x.Frequency);
    }

    public async Task<List<UserCustomTerm>> GetHighConfidenceTermsAsync(long chatId, decimal minConfidence = 0.7m, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        
        var sql = @"
            SELECT id, chat_id, user_term, standard_term, term_type, frequency, confidence,
                   first_seen, last_used, created_at, updated_at
            FROM user_custom_terms 
            WHERE chat_id = @ChatId AND confidence >= @MinConfidence
            ORDER BY confidence DESC, frequency DESC";

        var result = await connection.QueryAsync<UserCustomTerm>(sql, new { ChatId = chatId, MinConfidence = minConfidence });
        return result.ToList();
    }
}