using System;

namespace RegistroCx.Domain;

/// <summary>
/// OBSOLETO: Esta clase ha sido migrada a UserProfile.
/// Los datos de Telegram ahora se almacenan directamente en user_profiles.
/// Use UserProfile en su lugar.
/// </summary>
[Obsolete("UsuarioTelegram ha sido migrado a UserProfile. Use UserProfile en su lugar.", true)]
public class UsuarioTelegram
{
    public int Id { get; set; }
    public long? TelegramId { get; set; }
    public string? Nombre { get; set; }
    public string? Username { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public bool CalendarAutorizado { get; set; } = false;
    public long ChatId { get; set; }

    /// <summary>
    /// Helper para obtener nombre de display
    /// </summary>
    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(Nombre))
            return Nombre;
            
        if (!string.IsNullOrWhiteSpace(Username))
            return $"@{Username}";
            
        return "Usuario";
    }
}