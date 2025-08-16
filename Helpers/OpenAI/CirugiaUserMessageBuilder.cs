using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace RegistroCx.Helpers.OpenAI;

public static class CirugiaUserMessageBuilder
{
    private static readonly JsonSerializerOptions ListasJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Construye el contenido del mensaje user para el Assistant (Opción C).
    /// </summary>
    /// <param name="fechaHoy">Fecha de referencia (timezone local) usada para las reglas de fecha.</param>
    /// <param name="listasObj">Objeto con listas dinámicas (o null para enviar JSON vacío {}).</param>
    /// <param name="inputCirugiaRaw">Texto crudo recibido (agenda / mensaje).</param>
    /// <param name="metadatosExtra">Diccionario opcional con metadatos (canal, origen, etc.).</param>
    /// <param name="incluirEjemploSeccionMetadatos">Si true y hay metadatos, incluye bloque METADATOS delimitado.</param>
    /// <param name="contextPersonalizado">Contexto personalizado de aprendizaje del usuario (opcional).</param>
    /// <returns>String a usar como contenido del mensaje user.</returns>
    public static string Build(
        DateTime fechaHoy,
        object? listasObj,
        string inputCirugiaRaw,
        IDictionary<string, string>? metadatosExtra = null,
        bool incluirEjemploSeccionMetadatos = true,
        string? contextPersonalizado = null)
    {
        var fechaHoyStr = fechaHoy.ToString("dd/MM/yyyy");

        string listasJson = listasObj is null
            ? "{}"
            : JsonSerializer.Serialize(listasObj, ListasJsonOptions);

        // Sanitización mínima del input (podés ampliarla según tus necesidades)
        string inputSanitizado = inputCirugiaRaw
            .Replace("\r", "")
            .Trim();

        var sb = new StringBuilder();

        sb.AppendLine($"FECHA_HOY={fechaHoyStr}");
        sb.AppendLine("LISTAS_JSON=");
        sb.AppendLine(listasJson);
        sb.AppendLine();

        // Contexto personalizado del sistema de aprendizaje
        if (!string.IsNullOrWhiteSpace(contextPersonalizado))
        {
            sb.AppendLine("=== CONTEXTO_PERSONALIZADO_INICIO ===");
            sb.AppendLine(contextPersonalizado);
            sb.AppendLine("=== CONTEXTO_PERSONALIZADO_FIN ===");
            sb.AppendLine();
        }

        if (metadatosExtra != null && metadatosExtra.Count > 0 && incluirEjemploSeccionMetadatos)
        {
            sb.AppendLine("=== METADATOS_INICIO ===");
            foreach (var kv in metadatosExtra)
            {
                // Línea simple clave=valor (escapá saltos de línea si hiciera falta)
                var valueClean = kv.Value.Replace("\n", " ").Replace("\r", " ").Trim();
                sb.AppendLine($"{kv.Key}={valueClean}");
            }
            sb.AppendLine("=== METADATOS_FIN ===");
            sb.AppendLine();
        }

        sb.AppendLine("=== INPUT_CIRUGIA_INICIO ===");
        sb.AppendLine(inputSanitizado);
        sb.AppendLine("=== INPUT_CIRUGIA_FIN ===");
        sb.AppendLine();
        sb.AppendLine("INSTRUCCIONES_FINALES: Devuelve SOLO el JSON.");

        return sb.ToString();
    }
}

public class ListaItem
{
    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = "";
    [JsonPropertyName("alias")]
    public List<string> Alias { get; set; } = new();
    public string? Email { get; set; }   // ← NUEVO
}

public class ListasInternas
{
    [JsonPropertyName("anestesiologos")]
    public List<ListaItem> Anestesiologos { get; set; } = new();
    [JsonPropertyName("cirujanos")]
    public List<ListaItem> Cirujanos { get; set; } = new();
    [JsonPropertyName("lugares")]
    public List<ListaItem> Lugares { get; set; } = new();
    [JsonPropertyName("codigos_no_persona")]
    public List<string> CodigosNoPersona { get; set; } = new() { "CERS", "CX" };
}

public class ListasReferencia
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
    [JsonPropertyName("fecha_actualizacion")]
    public string FechaActualizacion { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    [JsonPropertyName("listas")]
    public ListasInternas Listas { get; set; } = new();
}
