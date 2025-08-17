using System;
using System.Collections.Generic;

namespace RegistroCx.Services;

/// <summary>
/// Servicio para detectar y mapear zonas horarias basado en información de Telegram
/// </summary>
public static class TimeZoneDetectionService
{
    /// <summary>
    /// Mapeo de códigos de idioma de Telegram a zonas horarias IANA
    /// </summary>
    private static readonly Dictionary<string, string> LanguageToTimeZone = new()
    {
        // Español - países de habla hispana
        ["es-AR"] = "America/Argentina/Buenos_Aires",
        ["es-CL"] = "America/Santiago",
        ["es-CO"] = "America/Bogota",
        ["es-MX"] = "America/Mexico_City",
        ["es-PE"] = "America/Lima",
        ["es-UY"] = "America/Montevideo",
        ["es-VE"] = "America/Caracas",
        ["es-ES"] = "Europe/Madrid",
        ["es"] = "America/Argentina/Buenos_Aires", // Default para español
        
        // Inglés
        ["en-US"] = "America/New_York",
        ["en-GB"] = "Europe/London",
        ["en-CA"] = "America/Toronto",
        ["en-AU"] = "Australia/Sydney",
        ["en"] = "America/New_York", // Default para inglés
        
        // Portugués
        ["pt-BR"] = "America/Sao_Paulo",
        ["pt-PT"] = "Europe/Lisbon",
        ["pt"] = "America/Sao_Paulo", // Default para portugués
        
        // Francés
        ["fr-FR"] = "Europe/Paris",
        ["fr-CA"] = "America/Montreal",
        ["fr"] = "Europe/Paris", // Default para francés
        
        // Alemán
        ["de-DE"] = "Europe/Berlin",
        ["de-AT"] = "Europe/Vienna",
        ["de-CH"] = "Europe/Zurich",
        ["de"] = "Europe/Berlin", // Default para alemán
        
        // Italiano
        ["it-IT"] = "Europe/Rome",
        ["it"] = "Europe/Rome",
        
        // Ruso
        ["ru-RU"] = "Europe/Moscow",
        ["ru"] = "Europe/Moscow",
        
        // Japonés
        ["ja-JP"] = "Asia/Tokyo",
        ["ja"] = "Asia/Tokyo",
        
        // Chino
        ["zh-CN"] = "Asia/Shanghai",
        ["zh-TW"] = "Asia/Taipei",
        ["zh"] = "Asia/Shanghai",
        
        // Coreano
        ["ko-KR"] = "Asia/Seoul",
        ["ko"] = "Asia/Seoul",
        
        // Árabe
        ["ar-SA"] = "Asia/Riyadh",
        ["ar-EG"] = "Africa/Cairo",
        ["ar"] = "Asia/Riyadh",
        
        // Hindi
        ["hi-IN"] = "Asia/Kolkata",
        ["hi"] = "Asia/Kolkata"
    };

    /// <summary>
    /// Detecta la zona horaria más probable basada en el idioma de Telegram
    /// </summary>
    /// <param name="languageCode">Código de idioma de Telegram (ej: "es-AR", "en-US")</param>
    /// <returns>Identificador IANA de zona horaria</returns>
    public static string DetectTimeZone(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return "America/Argentina/Buenos_Aires"; // Default
        
        var normalizedLanguage = languageCode.Trim().ToLowerInvariant();
        
        // Buscar coincidencia exacta primero (ej: "es-ar")
        if (LanguageToTimeZone.TryGetValue(normalizedLanguage, out var exactMatch))
            return exactMatch;
        
        // Buscar solo el idioma base (ej: "es" para "es-ar")
        var languageBase = normalizedLanguage.Split('-')[0];
        if (LanguageToTimeZone.TryGetValue(languageBase, out var baseMatch))
            return baseMatch;
        
        // Default si no encuentra nada
        return "America/Argentina/Buenos_Aires";
    }

    /// <summary>
    /// Verifica si una zona horaria IANA es válida
    /// </summary>
    /// <param name="timeZoneId">Identificador de zona horaria IANA</param>
    /// <returns>True si es válida, False en caso contrario</returns>
    public static bool IsValidTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Obtiene la zona horaria con fallback seguro
    /// </summary>
    /// <param name="timeZoneId">Identificador de zona horaria preferido</param>
    /// <returns>Zona horaria válida o fallback</returns>
    public static string GetSafeTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return "America/Argentina/Buenos_Aires";
        
        return IsValidTimeZone(timeZoneId) ? timeZoneId : "America/Argentina/Buenos_Aires";
    }
}