using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace RegistroCx.Helpers;

public static class LLMGemini
{
    private static Kernel _kernel;
    private static KernelFunction _extractEntitiesFunc;
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string API_KEY = "AIzaSyAWezoSDGD0iBTTFvGFuh-K9EJ74aCnhv8"; // Replace with your Google API key
    private const string GEMINI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite-preview-06-17:generateContent";
    private const string PROMPT_TEMPLATE = @"
Eres un extractor de datos para agendas de cirugías.
Recibes un texto de entrada y devuelves un JSON con estas claves:
- Anio: Año actual (YYYY). Si el 'Mes' es diciembre y la fecha es futura, considera el cambio de año. 
- Mes: Mes de la fecha del INPUT.
- Dia: Dia de la fecha del INPUT (valor numerico entre 1 y 31)
- Hora: Hora de la fecha del INPUT (valor numerico entre 0 y 23)
- Minuto: Minuto de la fecha del INPUT (valor numerico entre 0 y 59)
- lugar: Lugar de la cirugía
- cirujano: Nombre del cirujano a cargo
- cantidad: Número de cirugías (entero)
- anestesio: Nombre del anestesiólogo a cargo

Si algún campo no está presente, pon su valor como null.

Texto de entrada:
{INPUT}
Devuelve solo el JSON, sin texto adicional, sin comillas adicionales, y sin formato Markdown (como ```json).";
    /// <summary>
    /// Inicializa el kernel.
    /// </summary>
    public static async Task InitializeAsync()
    {
        // Configura el Kernel
        var builder = Kernel.CreateBuilder();
        _kernel = builder.Build();

        RegisterExtractFunction();
    }

    /// <summary>
    /// Registra el prompt que extrae los campos y devuelve JSON.
    /// </summary>
    private static void RegisterExtractFunction()
    {
        _extractEntitiesFunc = _kernel.CreateFunctionFromPrompt(
            promptTemplate: PROMPT_TEMPLATE,
            functionName: "extractEntities",
            description: "Extracts surgery schedule data into JSON format"
        );
    }

    /// <summary>
    /// Ejecuta la función de extracción usando la API de Gemini y retorna el diccionario de campos.
    /// </summary>
    public static async Task<Dictionary<string, string?>> ExtractEntitiesAsync(string text)
    {
        if (_kernel == null)
            throw new InvalidOperationException(
                "El Kernel no fue inicializado. Llamá a InitializeAsync primero.");
        if (_extractEntitiesFunc == null)
            throw new InvalidOperationException(
                "La función de extracción no está registrada.");

        try
        {
            // Sustituye {INPUT} en el prompt template
            string prompt = PROMPT_TEMPLATE.Replace("{INPUT}", text);

            // Configura la solicitud a Gemini
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 512,
                    temperature = 0.7,
                    topP = 0.9
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{GEMINI_ENDPOINT}?key={API_KEY}")
            {
                Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
            };

            // Llama a la API de Gemini
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Extrae el contenido del JSON retornado
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GEMINI-RESPONSE-RAW] {responseBody}");
            var jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseBody);
            string json = jsonResponse.candidates[0].content.parts[0].text.ToString();
            // Limpia el JSON: elimina Markdown, comillas adicionales y caracteres no deseados
            json = json.Trim();
            json = Regex.Replace(json, @"^```json\n|\n```$", ""); // Elimina ```json y ```
            json = Regex.Replace(json, @"\n\s*", ""); // Elimina newlines y espacios
            json = json.Trim('"'); // Elimina comillas envolventes si las hay
            Console.WriteLine($"[GEMINI-RAW-CLEANED] {json}");

            // Valida que el JSON sea válido antes de deserializar
            if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("{") || !json.EndsWith("}"))
            {
                throw new JsonException("El texto recibido no es un JSON válido.");
            }

            // Deserializa el JSON a un diccionario intermedio
            var tempDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json);

            // Convierte todos los valores a string? para cumplir con el tipo esperado
            var result = new Dictionary<string, string?>();
            foreach (var kvp in tempDict)
            {
                result[kvp.Key] = kvp.Value?.ToString();

                if (kvp.Key == "Anio")
                {
                    int currentYear = DateTime.Now.Year;
                    int currentMonth = DateTime.Now.Month;
                    int anio;
                    if (int.TryParse(result[kvp.Key], out anio))
                    {
                        // Si el año no es el actual, lo ajusta
                        if (anio != currentYear)
                        {
                            // Si el mes actual es diciembre, considera el cambio de año
                            if (currentMonth == 12)
                            {
                                result[kvp.Key] = (currentYear + 1).ToString();
                            }
                            else
                            {
                                result[kvp.Key] = currentYear.ToString();
                            }
                        }
                    }
                }
            }

            if (result.ContainsKey("Anio") && result.ContainsKey("Mes") && result.ContainsKey("Dia") &&
                result.ContainsKey("Hora") && result.ContainsKey("Minuto"))
            {
                string? anio = result["Anio"];
                string? mes = result["Mes"];
                string? dia = result["Dia"];
                string? hora = result["Hora"];
                string? minuto = result["Minuto"];

                if (int.TryParse(anio, out int year) &&
                    int.TryParse(mes, out int month) &&
                    int.TryParse(dia, out int day) &&
                    int.TryParse(hora, out int hour) &&
                    int.TryParse(minuto, out int minute))
                {
                    try
                    {
                        var fecha = new DateTime(year, month, day, hour, minute, 0);
                        result["fecha"] = fecha.ToString("yyyy-MM-dd HH:mm");
                    }
                    catch
                    {
                        result["fecha"] = null;
                    }
                }
                else
                {
                    result["fecha"] = null;
                }
            }
            else
            {
                result["fecha"] = null;
            }
            return result;

        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"[GEMINI-ERROR] {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LLM-ERROR] {ex}");
            throw;
        }
    }
}