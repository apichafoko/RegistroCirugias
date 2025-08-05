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


}
