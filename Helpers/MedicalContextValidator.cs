using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Services.Extraction;

namespace RegistroCx.Helpers;

/// <summary>
/// Validador que detecta si un texto tiene contexto médico/quirúrgico relevante
/// </summary>
public class MedicalContextValidator
{
    private readonly LLMOpenAIAssistant _llm;
    
    public MedicalContextValidator(LLMOpenAIAssistant llm)
    {
        _llm = llm;
    }

    // Fallback: Palabras clave médicas/quirúrgicas básicas para casos de emergencia
    private static readonly HashSet<string> MedicalKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Procedimientos quirúrgicos
        "cers", "mld", "adenoides", "amigdalas", "amígdalas", "cesarea", "cesárea", 
        "apendicectomia", "apendicectomía", "colecistectomia", "colecistectomía", 
        "hernia", "hernioplastia", "hernioplastía", "laparoscopia", "laparoscopía",
        "artroscopia", "artroscopía", "endoscopia", "endoscopía", "endoscopicas", "endoscópicas",
        "ondoscopia", "ondoscopía", "ondoscopicas", "ondoscópicas", "biopsia",
        "nariz", "nasal", "nasales", "septoplastia", "septoplastía", "rinoplastia", "rinoplastía",
        "cirugía", "cirugia", "operacion", "operación", "quirofano", "quirófano",
        "intervencion", "intervención", "procedimiento", "extirpacion", "extirpación",
        
        // Especialidades médicas
        "cirujano", "anestesiologo", "anestesiólogo", "anestesia", "doctor", "dra", 
        "medico", "médico", "especialista", "traumatologo", "traumatólogo",
        "cardiologo", "cardiólogo", "ginecologo", "ginecólogo", "urologo", "urólogo",
        "otorrino", "neurocirujano", "plastico", "plástico",
        
        // Lugares médicos
        "hospital", "clinica", "clínica", "sanatorio", "centro", "medico", "médico",
        "quirofano", "quirófano", "sala", "pabellon", "pabellón", "instituto",
        "italiano", "aleman", "alemán", "britanico", "británico", "finochietto",
        "anchorena", "mater", "dei", "favaloro", "fleni", "callao",
        
        // Términos temporales médicos comunes
        "cirugia", "cirugía", "operación", "operacion", "programar", "agendar",
        "turno", "cita", "fecha", "horario", "programado", "agendado",
        
        // Números que pueden indicar cantidades de procedimientos
        "cantidad", "procedimientos", "intervenciones",
        
        // Términos de urgencia/programación
        "urgente", "emergencia", "programado", "electivo", "electiva",
        "mañana", "hoy", "pasado", "próximo", "siguiente", "proximo",
        
        // Anestesia y medicamentos
        "sedacion", "sedación", "general", "local", "epidural", "raquídea", "raquidea"
    };

    // Patrones de fecha/hora que indican contexto de programación médica
    private static readonly Regex[] DateTimePatterns = new[]
    {
        new Regex(@"\d{1,2}[/\-]\d{1,2}(?:[/\-]\d{2,4})?", RegexOptions.IgnoreCase), // dd/mm o dd/mm/yyyy
        new Regex(@"\d{1,2}:\d{2}", RegexOptions.IgnoreCase), // HH:mm
        new Regex(@"\d{1,2}h(?:s|oras?)?", RegexOptions.IgnoreCase), // 14hs, 14horas
        new Regex(@"(?:mañana|hoy|ayer|pasado)", RegexOptions.IgnoreCase) // fechas relativas
    };

    // Patrones numéricos que pueden indicar cantidades de procedimientos
    private static readonly Regex[] QuantityPatterns = new[]
    {
        new Regex(@"\d+\s*(?:cers|mld|adenoides|amígdalas|amigdalas)", RegexOptions.IgnoreCase),
        new Regex(@"(?:cers|mld|adenoides|amígdalas|amigdalas)\s*x\s*\d+", RegexOptions.IgnoreCase),
        new Regex(@"\d+\s*(?:cirug[íi]as?|procedimientos?|intervenciones?)", RegexOptions.IgnoreCase)
    };

    /// <summary>
    /// Determina si el texto contiene contexto médico/quirúrgico relevante usando LLM
    /// </summary>
    public async Task<bool> HasMedicalContextAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            // Usar LLM para detectar contexto médico usando prompt publicado
            var response = await _llm.GetCompletionAsync(input, ct);
            var cleanResponse = response?.Trim().ToUpperInvariant();
            
            return cleanResponse == "SI" || cleanResponse == "SÍ" || cleanResponse == "YES";
        }
        catch (Exception ex)
        {
            // Fallback a método anterior si falla el LLM
            Console.WriteLine($"[MEDICAL-VALIDATOR] LLM failed, using fallback: {ex.Message}");
            return HasMedicalContextFallback(input);
        }
    }

    /// <summary>
    /// Método fallback usando palabras clave si el LLM falla
    /// </summary>
    private bool HasMedicalContextFallback(string input)
    {
        var normalizedInput = NormalizeText(input);
        var words = ExtractWords(normalizedInput);

        // Verificar palabras clave médicas
        if (HasMedicalKeywords(words))
            return true;

        // Verificar patrones de fecha/hora (pueden indicar programación médica)
        if (HasDateTimePatterns(normalizedInput))
            return true;

        // Verificar patrones de cantidad de procedimientos
        if (HasQuantityPatterns(normalizedInput))
            return true;

        return false;
    }

    /// <summary>
    /// Genera un mensaje de ayuda para texto no médico
    /// </summary>
    public string GenerateHelpMessage()
    {
        return "🤔 **No entiendo ese tipo de mensaje.**\n\n" +
               "📋 **Soy tu asistente de cirugías.** Puedo ayudarte con:\n\n" +
               "✅ **Ejemplos de mensajes correctos:**\n" +
               "• `2 CERS + 1 MLD mañana 14hs Hospital Italiano Dr. García López`\n" +
               "• `Apendicectomía 15/08 16:30 Sanatorio Anchorena`\n" +
               "• `3 adenoides hoy Clínica Santa Isabel`\n" +
               "• `Cesárea programada 20/08 9hs con Dr. Rodríguez`\n\n" +
               "🎤 **También podés enviar un mensaje de voz** describiendo tu cirugía.\n\n" +
               "❓ **Comandos disponibles:**\n" +
               "• `/ayuda` - Ver esta ayuda\n" +
               "• `/semanal` - Reporte de esta semana\n" +
               "• `/mensual` - Reporte mensual\n" +
               "• `/anual` - Reporte anual\n\n" +
               "💡 **Tip:** Incluí fecha, hora, lugar, cirujano y tipo de cirugía para mejores resultados.";
    }

    /// <summary>
    /// Genera un mensaje específico para texto claramente no médico
    /// </summary>
    public string GenerateNonMedicalMessage(string userInput)
    {
        var examples = new[]
        {
            "\"2 CERS mañana 14hs Hospital Italiano\"",
            "\"Apendicectomía 15/08 Dr. García\"", 
            "\"3 adenoides hoy Clínica Santa Isabel\"",
            "\"Cesárea programada 20/08 9hs\""
        };

        var randomExample = examples[new Random().Next(examples.Length)];

        return $"🤔 **\"{userInput}\" no parece ser información sobre una cirugía.**\n\n" +
               "📋 **Soy tu asistente especializado en cirugías.** Necesito información como:\n" +
               "• Tipo de cirugía (CERS, MLD, apendicectomía, etc.)\n" +
               "• Fecha y hora\n" +
               "• Hospital o clínica\n" +
               "• Cirujano y anestesiólogo\n\n" +
               $"✨ **Ejemplo:** {randomExample}\n\n" +
               "🎤 **También podés enviar un mensaje de voz** describiendo tu cirugía.\n\n" +
               "❓ Escribí `/ayuda` para ver más ejemplos y comandos disponibles.";
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text
            .ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i")
            .Replace("ó", "o").Replace("ú", "u").Replace("ñ", "n")
            .Trim();
    }

    private static List<string> ExtractWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // Extraer palabras alfanuméricas, manteniendo números
        return Regex.Matches(text, @"\b\w+\b")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(word => word.Length > 1) // Filtrar palabras muy cortas
            .ToList();
    }

    private static bool HasMedicalKeywords(List<string> words)
    {
        return words.Any(word => MedicalKeywords.Contains(word));
    }

    private static bool HasDateTimePatterns(string text)
    {
        return DateTimePatterns.Any(pattern => pattern.IsMatch(text));
    }

    private static bool HasQuantityPatterns(string text)
    {
        return QuantityPatterns.Any(pattern => pattern.IsMatch(text));
    }

    /// <summary>
    /// Detecta si el texto son solo palabras inconexas (como "perro verde", "edificio alto")
    /// </summary>
    public bool IsDisconnectedWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var words = ExtractWords(NormalizeText(input));
        
        // Si tiene menos de 2 palabras, no es texto inconexo
        if (words.Count < 2)
            return false;

        // Si tiene contexto médico, no es inconexo para nuestros propósitos  
        if (HasMedicalContextFallback(input))
            return false;

        // Si tiene patrones de fecha/hora/números, podría ser relevante
        if (HasDateTimePatterns(input) || HasQuantityPatterns(input))
            return false;

        // Lista de palabras comunes que no tienen contexto médico
        var commonNonMedicalWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "perro", "gato", "casa", "auto", "coche", "carro", "verde", "azul", "rojo", 
            "alto", "bajo", "grande", "pequeño", "edificio", "árbol", "mesa", "silla",
            "comida", "agua", "libro", "película", "música", "deporte", "fútbol",
            "trabajo", "oficina", "escuela", "universidad", "parque", "playa",
            "tiempo", "clima", "lluvia", "sol", "noche", "día", "semana",
            "dinero", "comprar", "vender", "mercado", "tienda", "restaurant",
            "familia", "amigo", "padre", "madre", "hijo", "hermano", "hermana"
        };

        // Si la mayoría de las palabras son no-médicas y comunes, probablemente es texto inconexo
        var nonMedicalCount = words.Count(word => commonNonMedicalWords.Contains(word));
        var ratio = (double)nonMedicalCount / words.Count;

        return ratio > 0.5; // Más del 50% son palabras no médicas comunes
    }
}