using System;

namespace RegistroCx.Domain;

/// <summary>
/// Representa la información específica de un usuario de Telegram
/// Almacenada en la tabla usuarios_telegram
/// </summary>
public class UsuarioTelegram
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
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