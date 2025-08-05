using System;

using Dapper;
using Npgsql;
namespace RegistroCx.BaseDeDatos;

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

public class UserProfileRepository : IUserProfileRepository
{
    private readonly string _connString;
    public UserProfileRepository(string connString) => _connString = connString;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<UserProfile?> GetAsync(long chatId, CancellationToken ct = default)
    {
        const string sql = @"SELECT chat_id      AS ChatId,
                                state        AS State,
                                phone        AS Phone,
                                google_email AS GoogleEmail,
                                google_access_token AS GoogleAccessToken,
                                google_refresh_token AS GoogleRefreshToken,
                                google_token_expiry AS GoogleTokenExpiry,
                                oauth_nonce  AS OAuthNonce,
                                created_at   AS CreatedAt,
                                updated_at   AS UpdatedAt
                            FROM user_profiles
                            WHERE chat_id=@chatId";
        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<UserProfile>(new CommandDefinition(sql, new { chatId }, cancellationToken: ct));
    }

    public async Task<UserProfile> GetOrCreateAsync(long chatId, CancellationToken ct = default)
    {
        var existing = await GetAsync(chatId, ct);
        if (existing != null) return existing;

        var profile = new UserProfile
        {
            ChatId = chatId,
            State = UserState.NeedPhone
        };

        const string insert = @"INSERT INTO user_profiles (chat_id, state)
                                VALUES (@ChatId, @State);";
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(insert, profile, cancellationToken: ct));
        return profile;
    }

    public async Task SaveAsync(UserProfile profile, CancellationToken ct = default)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        const string upsert = @"
            INSERT INTO user_profiles
                (chat_id, state, phone, google_email, google_access_token, google_refresh_token,
                google_token_expiry, oauth_nonce, created_at, updated_at)
            VALUES
                (@ChatId, @State, @Phone, @GoogleEmail, @GoogleAccessToken, @GoogleRefreshToken,
                @GoogleTokenExpiry, @OAuthNonce, COALESCE(@CreatedAt, now()), now())
            ON CONFLICT (chat_id) DO UPDATE SET
                state = EXCLUDED.state,
                phone = EXCLUDED.phone,
                google_email = EXCLUDED.google_email,
                google_access_token = EXCLUDED.google_access_token,
                google_refresh_token = COALESCE(EXCLUDED.google_refresh_token, user_profiles.google_refresh_token),
                google_token_expiry = EXCLUDED.google_token_expiry,
                oauth_nonce = EXCLUDED.oauth_nonce,
                updated_at = now();";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(upsert, profile, cancellationToken: ct));
    }

    public async Task SetStateNonceAsync(long chatId, string nonce, CancellationToken ct = default)
    {
        const string sql = @"UPDATE user_profiles
                             SET oauth_state_nonce = @nonce, updated_utc = NOW()
                             WHERE chat_id = @chatId;";
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { chatId, nonce }, cancellationToken: ct));
    }

    public async Task<long?> GetChatIdByNonceAsync(string nonce, CancellationToken ct = default)
    {
        const string sql = @"SELECT chat_id FROM user_profiles WHERE oauth_state_nonce = @nonce;";
        await using var conn = await OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { nonce }, cancellationToken: ct));
    }

    public async Task StoreOAuthStateAsync(long chatId, string nonce, CancellationToken ct)
    {
        const string sql = @"UPDATE user_profiles SET oauth_nonce=@nonce, updated_at=now() WHERE chat_id=@chatId";
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { chatId, nonce });
    }

    public async Task UpdateTokensAsync(long chatId, string access, string? refresh, DateTime? expiry, CancellationToken ct)
    {
        const string sql = @"
        UPDATE user_profiles
        SET google_access_token = @access,
            google_refresh_token = COALESCE(@refresh, google_refresh_token),
            google_token_expiry = @expiry,
            oauth_nonce = NULL,
            updated_at = now()
        WHERE chat_id = @chatId";
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { chatId, access, refresh, expiry });
    }

}

