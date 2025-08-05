using System;

namespace RegistroCx.Helpers._0Auth;

public interface IGoogleOAuthService
{
    /// <summary>
    /// Construye la URL de autorización con un nonce persistido (state).
    /// </summary>
    string BuildAuthUrl(long chatId, string userEmail);

    /// <summary>
    /// Intercambia el code + state por tokens; valida el nonce y guarda tokens.
    /// </summary>
    Task<TokenResponse?> ExchangeCodeAsync(string code, string state, CancellationToken ct);

    /// <summary>
    /// Refresca un access token usando el refresh token.
    /// </summary>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct);
}
