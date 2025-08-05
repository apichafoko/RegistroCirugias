using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RegistroCx.Helpers.OpenAI;

public class LLMOpenAIAssistant
{
    private readonly HttpClient _http;
    private readonly string _assistantId;
    private readonly string _apiKey;

    public LLMOpenAIAssistant(string apiKey, string assistantId, HttpClient? httpClient = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _assistantId = assistantId ?? throw new ArgumentNullException(nameof(assistantId));
        _http = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
        _http.DefaultRequestHeaders.Remove("OpenAI-Beta");
        _http.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
    }

    /// <summary>
    /// Extrae entidades desde un texto de agenda usando el Assistant.
    /// Devuelve un diccionario con claves: dia, mes, anio, hora, lugar, cirujano, anestesiologo, cantidad, notas.
    /// </summary>
    public async Task<Dictionary<string,string>> ExtractEntitiesAsync(
        string inputTexto,
        DateTime fechaHoy,
        ListasReferencia? listas = null,
        IDictionary<string,string>? metadatos = null)
    {
        // 1. Construir mensaje user (Opción C)
        var userMessage = CirugiaUserMessageBuilder.Build(
            fechaHoy: fechaHoy,
            listasObj: listas,
            inputCirugiaRaw: inputTexto,
            metadatosExtra: metadatos
        );

        // 2. Crear thread
        var thread = await PostJsonAsync("/v1/threads", new { });
        var threadId = thread.GetProperty("id").GetString()!;

        // 3. Agregar mensaje usuario
        await PostJsonAsync($"/v1/threads/{threadId}/messages", new
        {
            role = "user",
            content = userMessage
        });

        // 4. Run
        var run = await PostJsonAsync($"/v1/threads/{threadId}/runs", new
        {
            assistant_id = _assistantId
        });
        var runId = run.GetProperty("id").GetString()!;

        // 5. Poll
        while (true)
        {
            await Task.Delay(700);
            var runStatus = await GetJsonAsync($"/v1/threads/{threadId}/runs/{runId}");
            var status = runStatus.GetProperty("status").GetString();
            if (status == "completed") break;
            if (status == "failed" || status == "cancelled" || status == "expired")
                throw new Exception($"Run terminó con estado {status}");
        }

        // 6. Recuperar salida (últimos mensajes)
        var msgs = await GetJsonAsync($"/v1/threads/{threadId}/messages?limit=10");
        string? rawJson = null;

        foreach (var msg in msgs.GetProperty("data").EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
            {
                foreach (var content in msg.GetProperty("content").EnumerateArray())
                {
                    var type = content.GetProperty("type").GetString();
                    if (type == "output_text" || type == "text")
                    {
                        rawJson = content.GetProperty("text").GetProperty("value").GetString();
                        if (!string.IsNullOrWhiteSpace(rawJson))
                            break;
                    }
                }
            }
            if (rawJson != null) break;
        }

        if (rawJson == null)
            throw new Exception("No se obtuvo respuesta del assistant.");

        rawJson = rawJson.Trim();
        
        // Limpieza básica del JSON recibido
        rawJson = rawJson.Trim();
        rawJson = Regex.Replace(rawJson, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase);
        rawJson = Regex.Replace(rawJson, @"\r?\n\s*", "");
        rawJson = rawJson.Trim('"');

        // 7. Intentar parsear
        Dictionary<string, string> dict;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dia"] = root.TryGetProperty("dia", out var v1) ? v1.GetString() ?? "" : "",
                ["mes"] = root.TryGetProperty("mes", out var v2) ? v2.GetString() ?? "" : "",
                ["anio"] = root.TryGetProperty("anio", out var v3) ? v3.GetString() ?? "" : "",
                ["hora"] = root.TryGetProperty("hora", out var v4) ? v4.GetString() ?? "" : "",
                ["lugar"] = root.TryGetProperty("lugar", out var v5) ? v5.GetString() ?? "" : "",
                ["cirujano"] = root.TryGetProperty("cirujano", out var v6) ? v6.GetString() ?? "" : "",
                ["anestesiologo"] = root.TryGetProperty("anestesiologo", out var v7) ? v7.GetString() ?? "" : "",
                ["cantidad"] = root.TryGetProperty("cantidad", out var v8) ? v8.GetString() ?? "" : "",
                ["notas"] = root.TryGetProperty("notas", out var v9) ? v9.GetString() ?? "" : "",
                ["cirugia"] = root.TryGetProperty("cirugia", out var v10) ? v10.GetString() ?? "" : "",
            };
        }
        catch (Exception ex)
        {
            throw new Exception("La salida del assistant no es un JSON válido. Contenido bruto: " + rawJson, ex);
        }

        return dict;
    }

    /// <summary>
    /// Helper para reconstruir un DateTime final a partir de dia/mes/anio/hora (devuelve null si faltan campos esenciales).
    /// </summary>
    public static DateTime? ComposeFecha(Dictionary<string,string> dict)
    {
        if (!dict.TryGetValue("dia", out var d) ||
            !dict.TryGetValue("mes", out var m) ||
            !dict.TryGetValue("anio", out var y) ||
            !dict.TryGetValue("hora", out var h)) return null;

        if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(m) ||
            string.IsNullOrWhiteSpace(y) || string.IsNullOrWhiteSpace(h))
            return null;

        // hora HH:MM
        if (!TimeSpan.TryParse(h, out var ts))
            return null;

        if (!int.TryParse(d, out var day) ||
            !int.TryParse(m, out var month) ||
            !int.TryParse(y, out var year))
            return null;

        try
        {
            return new DateTime(year, month, day, ts.Hours, ts.Minutes, 0);
        }
        catch
        {
            return null;
        }
    }

    // ------------------ HTTP low-level helpers ------------------

    private async Task<JsonElement> PostJsonAsync(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com" + path);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Error {resp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com" + path);
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Error {resp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }
}
