using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RegistroCx.Services.Extraction;

public static class JSONExtractor
{
    public static Dictionary<string, string> ParseLLMResponse(string response)
    {
        try 
        {
            // Intentar parsear directo primero
            return JsonSerializer.Deserialize<Dictionary<string, string>>(response) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // Si falla, extraer JSON limpio y reintentar
            try 
            {
                var jsonLimpio = ExtraerJSON(response);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(jsonLimpio) ?? new Dictionary<string, string>();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ Error parseando JSON del LLM: {ex.Message}");
                Console.WriteLine($"📄 Respuesta original: {response}");
                Console.WriteLine($"🧹 JSON extraído: {ExtraerJSON(response)}");
                return new Dictionary<string, string>();
            }
        }
    }

    private static string ExtraerJSON(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "{}";

        // MÉTODO 1: Buscar el JSON principal balanceando llaves
        var inicio = response.IndexOf('{');
        if (inicio == -1)
            return "{}";

        int contador = 0;
        int fin = inicio;

        for (int i = inicio; i < response.Length; i++)
        {
            char c = response[i];
            
            if (c == '{')
            {
                contador++;
            }
            else if (c == '}')
            {
                contador--;
                if (contador == 0)
                {
                    fin = i;
                    break;
                }
            }
            else if (c == '"')
            {
                // Saltar strings para evitar llaves dentro de strings
                i = SaltarString(response, i);
            }
        }

        if (contador == 0 && fin > inicio)
        {
            var jsonExtraido = response.Substring(inicio, fin - inicio + 1);
            
            // Validar que sea JSON válido básico
            if (EsJSONBasicoValido(jsonExtraido))
            {
                return jsonExtraido;
            }
        }

        // MÉTODO 2: Fallback - buscar líneas que parezcan JSON
        var lineas = response.Split('\n');
        var jsonLines = new List<string>();
        bool dentroJSON = false;

        foreach (var linea in lineas)
        {
            var lineaTrim = linea.Trim();
            
            if (lineaTrim.StartsWith("{"))
            {
                dentroJSON = true;
                jsonLines.Add(lineaTrim);
            }
            else if (dentroJSON && lineaTrim.StartsWith("\""))
            {
                jsonLines.Add(lineaTrim);
            }
            else if (dentroJSON && lineaTrim.StartsWith("}"))
            {
                jsonLines.Add(lineaTrim);
                break;
            }
        }

        if (jsonLines.Count > 0)
        {
            return string.Join("", jsonLines);
        }

        // MÉTODO 3: Último recurso - JSON vacío
        Console.WriteLine("⚠️ No se pudo extraer JSON válido, devolviendo objeto vacío");
        return "{}";
    }

    private static int SaltarString(string text, int startIndex)
    {
        int i = startIndex + 1; // Saltar la primera comilla
        
        while (i < text.Length)
        {
            if (text[i] == '"' && (i == startIndex + 1 || text[i - 1] != '\\'))
            {
                return i; // Encontró el cierre de string
            }
            i++;
        }
        
        return text.Length - 1; // Si no encuentra cierre, va al final
    }

    private static bool EsJSONBasicoValido(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        json = json.Trim();
        
        // Debe empezar con { y terminar con }
        if (!json.StartsWith("{") || !json.EndsWith("}"))
            return false;

        // Contar llaves balanceadas
        int contador = 0;
        bool dentroString = false;
        
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            
            if (c == '"' && (i == 0 || json[i - 1] != '\\'))
            {
                dentroString = !dentroString;
            }
            else if (!dentroString)
            {
                if (c == '{') contador++;
                else if (c == '}') contador--;
            }
        }

        return contador == 0;
    }
}

// EXTENSIÓN para tu LLMOpenAIAssistant existente
public static class LLMResponseExtensions
{
    public static Dictionary<string, string> ParseSafely(this string llmResponse)
    {
        return JSONExtractor.ParseLLMResponse(llmResponse);
    }
}