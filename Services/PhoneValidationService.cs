using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RegistroCx.Services;

/// <summary>
/// Servicio para validación y normalización universal de números telefónicos
/// </summary>
public static class PhoneValidationService
{
    /// <summary>
    /// Resultado de validación telefónica
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? NormalizedPhone { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SuggestedFormat { get; set; }
        public string? CountryDetected { get; set; }
    }

    /// <summary>
    /// Configuración de país para validación telefónica
    /// </summary>
    private class CountryPhoneConfig
    {
        public string CountryCode { get; set; } = "";
        public string CountryName { get; set; } = "";
        public List<string> TimeZones { get; set; } = new();
        public List<string> LanguageCodes { get; set; } = new();
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public List<string> AreaCodes { get; set; } = new();
        public string FormatExample { get; set; } = "";
        public List<string> CommonMistakes { get; set; } = new();
    }

    /// <summary>
    /// Configuraciones por país
    /// </summary>
    private static readonly Dictionary<string, CountryPhoneConfig> CountryConfigs = new()
    {
        ["AR"] = new CountryPhoneConfig
        {
            CountryCode = "+54",
            CountryName = "Argentina",
            TimeZones = new List<string> { "America/Argentina/Buenos_Aires", "America/Argentina/Cordoba" },
            LanguageCodes = new List<string> { "es-AR", "es" },
            MinLength = 10,
            MaxLength = 11,
            AreaCodes = new List<string> { "11", "221", "223", "261", "341", "351", "381", "385" },
            FormatExample = "+5411XXXXXXXX",
            CommonMistakes = new List<string> { "+54911", "+549" } // Agregar 9 cuando ya está en el código
        },
        ["US"] = new CountryPhoneConfig
        {
            CountryCode = "+1",
            CountryName = "Estados Unidos",
            TimeZones = new List<string> { "America/New_York", "America/Los_Angeles", "America/Chicago", "America/Denver" },
            LanguageCodes = new List<string> { "en-US", "en", "es-US" },
            MinLength = 10,
            MaxLength = 10,
            AreaCodes = new List<string> { "212", "310", "415", "202", "305" },
            FormatExample = "+1XXXXXXXXXX",
            CommonMistakes = new List<string> { "+11" }
        },
        ["BR"] = new CountryPhoneConfig
        {
            CountryCode = "+55",
            CountryName = "Brasil",
            TimeZones = new List<string> { "America/Sao_Paulo", "America/Rio_Branco" },
            LanguageCodes = new List<string> { "pt-BR", "pt" },
            MinLength = 10,
            MaxLength = 11,
            AreaCodes = new List<string> { "11", "21", "31", "41", "51", "61", "71", "81", "85" },
            FormatExample = "+5511XXXXXXXXX",
            CommonMistakes = new List<string> { "+5511" }
        },
        ["MX"] = new CountryPhoneConfig
        {
            CountryCode = "+52",
            CountryName = "México",
            TimeZones = new List<string> { "America/Mexico_City", "America/Cancun" },
            LanguageCodes = new List<string> { "es-MX", "es" },
            MinLength = 10,
            MaxLength = 10,
            AreaCodes = new List<string> { "55", "33", "81", "222", "443" },
            FormatExample = "+52XXXXXXXXXX",
            CommonMistakes = new List<string> { "+521" }
        },
        ["CL"] = new CountryPhoneConfig
        {
            CountryCode = "+56",
            CountryName = "Chile",
            TimeZones = new List<string> { "America/Santiago" },
            LanguageCodes = new List<string> { "es-CL", "es" },
            MinLength = 8,
            MaxLength = 9,
            AreaCodes = new List<string> { "2", "32", "33", "34", "41", "42", "43" },
            FormatExample = "+56XXXXXXXXX",
            CommonMistakes = new List<string> { "+5696" }
        },
        ["CO"] = new CountryPhoneConfig
        {
            CountryCode = "+57",
            CountryName = "Colombia",
            TimeZones = new List<string> { "America/Bogota" },
            LanguageCodes = new List<string> { "es-CO", "es" },
            MinLength = 10,
            MaxLength = 10,
            AreaCodes = new List<string> { "1", "2", "4", "5", "6", "7", "8" },
            FormatExample = "+57XXXXXXXXXX",
            CommonMistakes = new List<string> { "+571" }
        },
        ["ES"] = new CountryPhoneConfig
        {
            CountryCode = "+34",
            CountryName = "España",
            TimeZones = new List<string> { "Europe/Madrid" },
            LanguageCodes = new List<string> { "es-ES", "es" },
            MinLength = 9,
            MaxLength = 9,
            AreaCodes = new List<string> { "6", "7", "9" }, // Móviles empiezan con 6 o 7
            FormatExample = "+34XXXXXXXXX",
            CommonMistakes = new List<string> { "+346", "+347" }
        }
    };

    /// <summary>
    /// Detecta el país probable basado en timezone y idioma
    /// </summary>
    public static string DetectCountry(string? timeZone, string? languageCode)
    {
        // Buscar por timezone primero (más preciso)
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            foreach (var (countryCode, config) in CountryConfigs)
            {
                if (config.TimeZones.Any(tz => tz.Equals(timeZone, StringComparison.OrdinalIgnoreCase)))
                {
                    return countryCode;
                }
            }
        }

        // Buscar por idioma
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var normalizedLang = languageCode.Trim().ToLowerInvariant();
            foreach (var (countryCode, config) in CountryConfigs)
            {
                if (config.LanguageCodes.Any(lang => normalizedLang.StartsWith(lang)))
                {
                    return countryCode;
                }
            }
        }

        // Default a Argentina
        return "AR";
    }

    /// <summary>
    /// Valida y normaliza un número telefónico
    /// </summary>
    public static ValidationResult ValidateAndNormalize(string phoneInput, string? timeZone = null, string? languageCode = null)
    {
        if (string.IsNullOrWhiteSpace(phoneInput))
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = "Por favor ingresa tu número de teléfono."
            };
        }

        // Limpiar el input
        var cleanPhone = CleanPhoneNumber(phoneInput);
        
        // Detectar país
        var detectedCountry = DetectCountry(timeZone, languageCode);
        var config = CountryConfigs[detectedCountry];

        // Intentar normalizar
        var normalizedPhone = TryNormalizePhone(cleanPhone, config);
        
        if (!string.IsNullOrEmpty(normalizedPhone))
        {
            return new ValidationResult
            {
                IsValid = true,
                NormalizedPhone = normalizedPhone,
                CountryDetected = config.CountryName
            };
        }

        // Si falló, dar sugerencias específicas
        return GenerateErrorWithSuggestions(cleanPhone, config);
    }

    /// <summary>
    /// Limpia el número telefónico de caracteres no deseados
    /// </summary>
    private static string CleanPhoneNumber(string input)
    {
        // Remover espacios, guiones, paréntesis, etc. pero mantener +
        return Regex.Replace(input, @"[^\d+]", "");
    }

    /// <summary>
    /// Intenta normalizar el número según la configuración del país
    /// </summary>
    private static string? TryNormalizePhone(string cleanPhone, CountryPhoneConfig config)
    {
        // Caso 1: Ya tiene el código de país correcto
        if (cleanPhone.StartsWith(config.CountryCode))
        {
            var phoneWithoutCountry = cleanPhone.Substring(config.CountryCode.Length);
            
            // Argentina: Manejar el caso especial del +54911... -> +5411...
            if (config.CountryCode == "+54" && phoneWithoutCountry.StartsWith("9"))
            {
                phoneWithoutCountry = phoneWithoutCountry.Substring(1); // Remover el 9 extra
            }
            
            if (IsValidLength(phoneWithoutCountry, config))
            {
                return config.CountryCode + phoneWithoutCountry;
            }
        }

        // Caso 2: Número sin código de país
        if (!cleanPhone.StartsWith("+"))
        {
            // Argentina: Si empieza con 15, removerlo (es prefijo de celular local)
            if (config.CountryCode == "+54" && cleanPhone.StartsWith("15"))
            {
                cleanPhone = cleanPhone.Substring(2);
            }
            
            // Argentina: Si empieza con 0, removerlo (prefijo nacional)
            if (cleanPhone.StartsWith("0"))
            {
                cleanPhone = cleanPhone.Substring(1);
            }
            
            if (IsValidLength(cleanPhone, config))
            {
                return config.CountryCode + cleanPhone;
            }
        }

        // Caso 3: Intentar corregir errores comunes
        foreach (var mistake in config.CommonMistakes)
        {
            if (cleanPhone.StartsWith(mistake))
            {
                var corrected = config.CountryCode + cleanPhone.Substring(mistake.Length);
                var phoneWithoutCountry = corrected.Substring(config.CountryCode.Length);
                
                if (IsValidLength(phoneWithoutCountry, config))
                {
                    return corrected;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Verifica si la longitud del número es válida
    /// </summary>
    private static bool IsValidLength(string phoneWithoutCountry, CountryPhoneConfig config)
    {
        return phoneWithoutCountry.Length >= config.MinLength && phoneWithoutCountry.Length <= config.MaxLength;
    }

    /// <summary>
    /// Genera un error con sugerencias específicas
    /// </summary>
    private static ValidationResult GenerateErrorWithSuggestions(string cleanPhone, CountryPhoneConfig config)
    {
        var errorMessage = $"El formato del número no es válido para {config.CountryName}.";
        var suggestion = $"Ejemplo de formato correcto: {config.FormatExample}";

        // Sugerencias específicas basadas en el input
        if (cleanPhone.Length < 8)
        {
            errorMessage += " El número es muy corto.";
        }
        else if (cleanPhone.Length > 15)
        {
            errorMessage += " El número es muy largo.";
        }
        else if (!cleanPhone.StartsWith("+"))
        {
            errorMessage += " Asegúrate de incluir el código de país.";
            suggestion = $"Prueba agregando {config.CountryCode} al inicio: {config.CountryCode}XXXXXXXX";
        }

        return new ValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            SuggestedFormat = suggestion,
            CountryDetected = config.CountryName
        };
    }

    /// <summary>
    /// Formatea un número válido para mostrar de manera legible
    /// </summary>
    public static string FormatForDisplay(string normalizedPhone)
    {
        if (string.IsNullOrEmpty(normalizedPhone) || !normalizedPhone.StartsWith("+"))
            return normalizedPhone;

        // Formato básico: +XX XXX XXX XXXX
        var countryCode = normalizedPhone.Substring(0, 3); // +XX
        var number = normalizedPhone.Substring(3);
        
        if (number.Length >= 8)
        {
            // Agrupar dígitos para mejor legibilidad
            var groups = new List<string>();
            for (int i = 0; i < number.Length; i += 3)
            {
                var groupLength = Math.Min(3, number.Length - i);
                groups.Add(number.Substring(i, groupLength));
            }
            return $"{countryCode} {string.Join(" ", groups)}";
        }
        
        return normalizedPhone;
    }
}