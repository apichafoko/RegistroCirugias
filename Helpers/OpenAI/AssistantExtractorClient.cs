using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RegistroCx.Helpers.OpenAI;

public class AssistantExtractorClient
{
    private readonly HttpClient _http;
    private readonly string _assistantId;

    public AssistantExtractorClient(HttpClient http, string assistantId, string apiKey)
    {
        _http = http;
        _assistantId = assistantId;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
    }

    public async Task<string> ExtraerAsync(string userMessageContent)
    {
        // 1. Crear thread
        var thread = await PostJsonAsync("/v1/threads", new { });
        var threadId = thread.GetProperty("id").GetString()!;

        // 2. Añadir message user
        await PostJsonAsync($"/v1/threads/{threadId}/messages", new {
            role = "user",
            content = userMessageContent
        });

        // 3. Crear run
        var run = await PostJsonAsync($"/v1/threads/{threadId}/runs", new {
            assistant_id = _assistantId
        });
        var runId = run.GetProperty("id").GetString()!;

        // 4. Poll
        while (true)
        {
            await Task.Delay(700);
            var runStatus = await GetJsonAsync($"/v1/threads/{threadId}/runs/{runId}");
            var status = runStatus.GetProperty("status").GetString();
            if (status == "completed") break;
            if (status == "failed" || status == "cancelled" || status == "expired")
                throw new Exception($"Run status: {status}");
        }

        // 5. Obtener últimos mensajes del thread
        var msgs = await GetJsonAsync($"/v1/threads/{threadId}/messages?limit=5");
        foreach (var msg in msgs.GetProperty("data").EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant")
            {
                foreach (var content in msg.GetProperty("content").EnumerateArray())
                {
                    var type = content.GetProperty("type").GetString();
                    if (type == "output_text" || type == "text")
                    {
                        var value = content.GetProperty("text").GetProperty("value").GetString();
                        return value!.Trim();
                    }
                }
            }
        }

        throw new Exception("No se encontró salida del assistant.");
    }

    private async Task<JsonElement> PostJsonAsync(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com" + path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Error {resp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<JsonElement> GetJsonAsync(string path)
    {
        var resp = await _http.GetAsync("https://api.openai.com" + path);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Error {resp.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }
}
