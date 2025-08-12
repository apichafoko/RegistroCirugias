using System;

using Dapper;
using Npgsql;
using RegistroCx.Domain;
using NpgsqlTypes;


namespace RegistroCx.Services.Repositories;

public class UserProfileRepository : IUserProfileRepository
{
    private readonly string _connString;
    public UserProfileRepository(string connString) => _connString = connString;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {

        var csb = new NpgsqlConnectionStringBuilder();

        if (_connString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Parsear URI
            var uri = new Uri(_connString);
            var userInfo = uri.UserInfo.Split(':', 2);
            csb.Host = uri.Host;
            if (uri.Port > 0)              // sólo asigna si está explícito
                csb.Port = uri.Port;
            csb.Username = userInfo[0];
            csb.Password = userInfo.Length > 1 ? userInfo[1] : "";
            csb.Database = uri.AbsolutePath.TrimStart('/');
            csb.TrustServerCertificate = true;

            // leer parámetros de query
            var qp = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (qp["sslmode"] != null)
                csb.SslMode = Enum.Parse<SslMode>(qp["sslmode"]!, ignoreCase: true);
            
        }
        else
        {
            // asume ya viene en formato clave=valor
            csb.ConnectionString = _connString;
        }
        

        var conn = new NpgsqlConnection( csb.ConnectionString);


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
        var profile = await GetAsync(chatId, ct);
        if (profile == null)
        {
            profile = new UserProfile
            {
                ChatId = chatId,
                State = UserState.NeedPhone
            };

            const string insert = @"INSERT INTO user_profiles (chat_id, state)
                                    VALUES (@ChatId, @State);";
            await using var conn = await OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(insert, profile, cancellationToken: ct));
        }

        // Determinar el estado según los datos disponibles
        if (string.IsNullOrEmpty(profile.Phone))
        {
            profile.State = UserState.NeedPhone;
        }
        else if (string.IsNullOrEmpty(profile.GoogleEmail))
        {
            profile.State = UserState.NeedEmail;
        }
        //else if (string.IsNullOrEmpty(profile.GoogleAccessToken) || profile.GoogleTokenExpiry == null || profile.GoogleTokenExpiry <= DateTime.UtcNow)
        else if (string.IsNullOrEmpty(profile.GoogleAccessToken))
        {
            profile.State = UserState.NeedOAuth;
        }
        else
        {
            profile.State = UserState.Ready;
        }

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

    public async Task<UserProfile?> FindByPhoneAsync(string phone, CancellationToken ct = default)
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
                            WHERE phone = @phone";
        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<UserProfile>(new CommandDefinition(sql, new { phone }, cancellationToken: ct));
    }

    public async Task<UserProfile?> FindByEmailAsync(string email, CancellationToken ct = default)
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
                            WHERE google_email = @email";
        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<UserProfile>(new CommandDefinition(sql, new { email }, cancellationToken: ct));
    }

    public async Task LinkChatIdAsync(long originalChatId, long newChatId, CancellationToken ct = default)
    {
        const string sql = @"UPDATE user_profiles 
                            SET chat_id = @newChatId, 
                                updated_at = now()
                            WHERE chat_id = @originalChatId";
        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { originalChatId, newChatId }, cancellationToken: ct));
    }

    public async Task<UserProfile> CreateProfileCopyingEmailTokensAsync(UserProfile sourceProfile, long newChatId, CancellationToken ct = default)
    {
        var newProfile = new UserProfile
        {
            ChatId = newChatId,
            State = UserState.Ready, // Ya tiene email y tokens válidos
            
            // Copiar datos del email de equipo y tokens OAuth
            GoogleEmail = sourceProfile.GoogleEmail,
            GoogleAccessToken = sourceProfile.GoogleAccessToken,
            GoogleRefreshToken = sourceProfile.GoogleRefreshToken,
            GoogleTokenExpiry = sourceProfile.GoogleTokenExpiry,
            
            // NO copiar teléfono (cada usuario tiene su propio teléfono)
            Phone = null,
            
            // Los datos de Telegram se manejan en la tabla usuarios_telegram por separado
        };

        await SaveAsync(newProfile, ct);
        return newProfile;
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
