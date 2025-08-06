using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;

namespace RegistroCx.Helpers
{
    public static class LLMOpenAI
    {

    private static Kernel? _kernel;
    private static KernelFunction? _extractEntitiesFunc;
        private static readonly HttpClient _httpClient = new HttpClient();
        // Carga tu API Key de OpenAI desde una variable de entorno para mayor seguridad
        //private static readonly string ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        //?? throw new InvalidOperationException("Define la variable de entorno OPENAI_API_KEY");

        
        private static readonly string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
    ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        private const string ChatEndpoint = "https://api.openai.com/v1/chat/completions";

        private const string PROMPT_TEMPLATE = @"
        Eres un extractor de datos para agendas de cirugías.
        Recibes un texto de entrada y devuelves un JSON con estas claves:
        - fecha: Fecha y hora en formato ISO 8601 (YYYY-MM-DDTHH:MM). Si no indica el mes asumi que es la fecha mas proxima con ese numero. Por ejemplo si estamos a 16 de julio y dice 15 sin aclarar el mes, seria 15 de agosto. Por otro lado asumi siempre que el año es el año actual al menos que lo aclare. Si el mes de la fecha es diciembre tene en cuenta el cambio de año para fechas futuras.
        - lugar: Lugar de la cirugía
        - cirujano: Nombre del cirujano a cargo
        - cantidad: Número de cirugías (entero)
        - anestesio: Nombre del anestesiólogo a cargo

        Si algún campo no está presente, pon su valor como null.

        Devuelve solo el JSON, sin texto adicional, sin comillas adicionales, y sin formato Markdown.";

        /// <summary>
        /// Llama al API de ChatGPT para extraer entidades de un texto de entrada.
        /// </summary>
        /// <param name="text">El texto de entrada con información de la agenda de cirugías.</param>
        /// <returns>Un diccionario con las claves solicitadas y sus valores (o null si no aparecen).</returns>
        public static async Task<Dictionary<string, string?>> ExtractEntitiesAsync(string text)
        {
            // Construir el payload
            var requestPayload = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = PROMPT_TEMPLATE },
                    new { role = "user", content = text }
                },
                temperature = 0.7,
                max_tokens = 512,
                top_p = 0.9
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            // Enviar petición
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Leer y parsear respuesta
            var raw = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var content = doc.RootElement
                             .GetProperty("choices")[0]
                             .GetProperty("message")
                             .GetProperty("content")
                             .GetString() ?? throw new System.Text.Json.JsonException("No se recibió contenido en la respuesta.");

            // Limpieza básica del JSON recibido
            var json = content.Trim();
            json = Regex.Replace(json, @"^```json\s*|\s*```$", "", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\r?\n\s*", "");
            json = json.Trim('"');

            if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("{") || !json.EndsWith("}"))
                throw new System.Text.Json.JsonException($"El texto recibido no es un JSON válido: «{json}»");

            // Deserializar a diccionario genérico
            var tempDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                           ?? throw new System.Text.Json.JsonException("No se pudo deserializar el JSON a Dictionary.");

            // Convertir a Dictionary<string, string?>
            var result = new Dictionary<string, string?>();
            foreach (var kvp in tempDict)
            {
                if (kvp.Value.ValueKind == JsonValueKind.Null)
                {
                    result[kvp.Key] = null;
                }
                else
                {
                    result[kvp.Key] = kvp.Value.GetString();
                }
            }

            return result;
        }

        public static void Initialize()
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
        if (_kernel == null)
            throw new InvalidOperationException("_kernel is not initialized. Call Initialize() before registering functions.");

        _extractEntitiesFunc = _kernel.CreateFunctionFromPrompt(
            promptTemplate: PROMPT_TEMPLATE,
            functionName: "extractEntities",
            description: "Extracts surgery schedule data into JSON format"
        );
    }

    }
}
