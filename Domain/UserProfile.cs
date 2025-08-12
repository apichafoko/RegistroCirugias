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

    // Campos específicos de Telegram (únicos por usuario de Telegram)
    public long? TelegramUserId { get; set; }
    public string? TelegramFirstName { get; set; }
    public string? TelegramLastName { get; set; }
    public string? TelegramUsername { get; set; }
    public string? TelegramLanguageCode { get; set; }

    // Auditoría opcional
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Helper para obtener nombre completo de Telegram
    public string GetTelegramDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(TelegramFirstName) && !string.IsNullOrWhiteSpace(TelegramLastName))
            return $"{TelegramFirstName} {TelegramLastName}";
        
        if (!string.IsNullOrWhiteSpace(TelegramFirstName))
            return TelegramFirstName;
            
        if (!string.IsNullOrWhiteSpace(TelegramUsername))
            return $"@{TelegramUsername}";
            
        return "Usuario";
    }
}
