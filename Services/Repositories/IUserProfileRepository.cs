using System;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories;

public interface IUserProfileRepository
{
   
    Task<UserProfile> GetOrCreateAsync(long chatId, CancellationToken ct = default);
    Task<UserProfile?> GetAsync(long chatId, CancellationToken ct = default);
    Task SaveAsync(UserProfile profile, CancellationToken ct = default);
    Task SetStateNonceAsync(long chatId, string nonce, CancellationToken ct = default);
    Task<long?> GetChatIdByNonceAsync(string nonce, CancellationToken ct = default);

    Task StoreOAuthStateAsync(long chatId, string nonce, CancellationToken ct);
    Task UpdateTokensAsync(long chatId, string access, string? refresh, DateTime? expiry, CancellationToken ct);
    
    // Métodos para vinculación de perfiles existentes
    Task<UserProfile?> FindByPhoneAsync(string phone, CancellationToken ct = default);
    Task<UserProfile?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task LinkChatIdAsync(long originalChatId, long newChatId, CancellationToken ct = default);
    Task LinkChatIdByIdAsync(int profileId, long newChatId, CancellationToken ct = default);
    Task<UserProfile> CreateProfileCopyingEmailTokensAsync(UserProfile sourceProfile, long newChatId, CancellationToken ct = default);

    // Métodos de Telegram (migrados desde IUsuarioTelegramRepository)
    Task<UserProfile?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task UpdateTelegramDataAsync(
        long chatId,
        long? telegramId,
        string? nombre,
        string? username,
        string? telefono = null,
        string? email = null,
        string? timeZone = null,
        CancellationToken ct = default);
    Task UpdateTelegramDataByPhoneAsync(
        long chatId,
        long? telegramId,
        string? nombre,
        string? username,
        string telefono,
        string? timeZone = null,
        CancellationToken ct = default);

}
