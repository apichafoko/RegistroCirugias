using System;
using System.Text.RegularExpressions;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;
using Microsoft.Recognizers.Text.Number;

namespace RegistroCx.Helpers;

public class Recognizers
{
    public static DateTime? ExtractDateTimeFromText(string text)
    {
        var model = new DateTimeRecognizer()
            .GetDateTimeModel(Culture.Spanish);

        var results = model.Parse(text);
        foreach (var r in results)
        {
            if (r.TypeName.StartsWith("datetime"))
            {
                // `"values"` es una lista de dicts con "value" o "start"/"end"
                var values = (IList<Dictionary<string, string>>)r.Resolution["values"];
                if (values.Count > 0 && values[0].TryGetValue("value", out var val))
                    return DateTime.Parse(val);
            }
        }
        return null;
    }

public static int? ExtractNumberFromText(string text)
{
    var model = new NumberRecognizer()
        .GetNumberModel(Culture.Spanish);

    var results = model.Parse(text);
    if (results.Count > 0 && results[0].Resolution.TryGetValue("value", out var raw))
    {
        if (int.TryParse(raw.ToString(), out var num))
            return num;
    }
    return null;
}

// Implementaciones simples por regex; luego las mejoramos con LLM:
public static string? ExtractPlaceFromText(string text)
{
    // ej: "en Quirófano Central", "en sala 2", etc.
    var m = Regex.Match(text, @"\ben\s+([A-ZÁÉÍÓÚ][\w\s\-]+)");
    return m.Success ? m.Groups[1].Value.Trim() : null;
}

public static string? ExtractCirujanoFromText(string text)
{
    // ej: detecta "Dr. Pérez" o nombres propios
    var m = Regex.Match(text, @"\bDr\.?\s+([A-ZÁÉÍÓÚ][\w]+)");
    return m.Success ? "Dr. " + m.Groups[1].Value : null;
}

public static string? ExtractAnestesioFromText(string text)
{
    // por defecto dejamos vacío y preguntamos al final
    return null;
}
}
