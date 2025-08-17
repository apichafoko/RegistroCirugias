using System;

namespace RegistroCx.Domain;

public class UserProfile
{
    public int Id { get; set; }
    public long? ChatId { get; set; }
    public string? Phone { get; set; }  // Unificado: teléfono principal
    public string? GoogleEmail { get; set; }  // Unificado: email principal
    public UserState State { get; set; }
    public string? OauthProvider { get; set; } = "google";
    public string? OauthStateNonce { get; set; }

    // OAuth / Tokens (campos principales)
    public string? GoogleAccessToken { get; set; }
    public string? GoogleRefreshToken { get; set; }
    public DateTime? GoogleTokenExpiry { get; set; }
    public string? OAuthNonce { get; set; }

    // Campos de Telegram (migrados de usuarios_telegram)
    public long? TelegramUserId { get; set; }
    public string? TelegramFirstName { get; set; }
    public string? TelegramUsername { get; set; }
    public bool CalendarAutorizado { get; set; } = false;
    
    // Zona horaria del usuario (IANA timezone identifier como "America/Argentina/Buenos_Aires")
    public string? TimeZone { get; set; } = "America/Argentina/Buenos_Aires"; // Default para Argentina

    // Auditoría
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Helper para obtener nombre de display del usuario
    /// </summary>
    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(TelegramFirstName))
            return TelegramFirstName;
            
        if (!string.IsNullOrWhiteSpace(TelegramUsername))
            return $"@{TelegramUsername}";
            
        return "Usuario";
    }

    /// <summary>
    /// Helper para obtener el teléfono principal
    /// </summary>
    public string? GetPrimaryPhone()
    {
        return Phone;
    }

    /// <summary>
    /// Helper para obtener el email principal
    /// </summary>
    public string? GetPrimaryEmail()
    {
        return GoogleEmail;
    }
}
