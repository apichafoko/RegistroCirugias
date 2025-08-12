using System;

namespace RegistroCx.Domain;

public class UserProfile
{
    public long ChatId { get; set; }
    public string? Phone { get; set; }
    public string? GoogleEmail { get; set; }
    public UserState State { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? AccessExpiresUtc { get; set; }
    public string? OauthProvider { get; set; } = "google";
    public string? OauthStateNonce { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    // OAuth / Tokens
    public string? GoogleAccessToken { get; set; }
    public string? GoogleRefreshToken { get; set; }
    public DateTime? GoogleTokenExpiry { get; set; }
    public string? OAuthNonce { get; set; }

    // Nota: Los datos específicos de Telegram se almacenan en la tabla usuarios_telegram

    // Auditoría opcional
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

}
