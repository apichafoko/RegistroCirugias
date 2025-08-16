using System;
using System.Text.Json;
using System.Threading.Tasks;
using RegistroCx.models;
using RegistroCx.Models;
using RegistroCx.Services.Extraction;

namespace RegistroCx.Services
{
    public class AppointmentModificationService
    {
        private readonly LLMOpenAIAssistant _llm;

        public AppointmentModificationService(LLMOpenAIAssistant llm)
        {
            _llm = llm;
        }

        public async Task<ModificationRequest> ParseModificationAsync(Appointment original, string userRequest)
        {
            try
            {
                var originalDate = original.FechaHora?.ToString("dd/MM/yyyy") ?? "No definida";
                var originalTime = original.FechaHora?.ToString("HH:mm") ?? "No definida";

                var originalData = $"FECHA_ORIGINAL: {originalDate}\n" +
                                 $"HORA_ORIGINAL: {originalTime}\n" +
                                 $"LUGAR_ORIGINAL: {original.Lugar ?? "No definido"}\n" +
                                 $"CIRUJANO_ORIGINAL: {original.Cirujano ?? "No definido"}\n" +
                                 $"CIRUGIA_ORIGINAL: {original.Cirugia ?? "No definida"}\n" +
                                 $"CANTIDAD_ORIGINAL: {original.Cantidad}\n" +
                                 $"ANESTESIOLOGO_ORIGINAL: {original.Anestesiologo ?? "No definido"}";

                var response = await _llm.ParseModificationAsync(originalData, userRequest);
                
                return ParseLLMResponse(response);
            }
            catch (Exception ex)
            {
                // Log error but return empty modification to avoid breaking the flow
                Console.WriteLine($"Error parsing modification: {ex.Message}");
                return new ModificationRequest();
            }
        }

        private ModificationRequest ParseLLMResponse(string response)
        {
            var modification = new ModificationRequest();

            try
            {
                // Limpiar la respuesta para extraer solo el JSON
                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var jsonDoc = JsonDocument.Parse(jsonString);
                    var root = jsonDoc.RootElement;

                    // Parsear fecha
                    if (root.TryGetProperty("fecha", out var fechaElement))
                    {
                        var fechaStr = fechaElement.GetString();
                        if (!string.IsNullOrEmpty(fechaStr) && DateTime.TryParseExact(fechaStr, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var fecha))
                        {
                            modification.NewDate = fecha;
                        }
                    }

                    // Parsear hora
                    if (root.TryGetProperty("hora", out var horaElement))
                    {
                        var horaStr = horaElement.GetString();
                        if (!string.IsNullOrEmpty(horaStr) && TimeOnly.TryParseExact(horaStr, "HH:mm", out var hora))
                        {
                            modification.NewTime = hora;
                        }
                    }

                    // Parsear campos de texto
                    if (root.TryGetProperty("lugar", out var lugarElement))
                    {
                        modification.NewLocation = lugarElement.GetString();
                    }

                    if (root.TryGetProperty("cirujano", out var cirujanoElement))
                    {
                        modification.NewSurgeon = cirujanoElement.GetString();
                    }

                    if (root.TryGetProperty("cirugia", out var cirugiaElement))
                    {
                        modification.NewSurgeryType = cirugiaElement.GetString();
                    }

                    if (root.TryGetProperty("anestesiologo", out var anestElement))
                    {
                        modification.NewAnesthesiologist = anestElement.GetString();
                    }

                    // Parsear cantidad
                    if (root.TryGetProperty("cantidad", out var cantidadElement) && cantidadElement.TryGetInt32(out var cantidad))
                    {
                        modification.NewQuantity = cantidad;
                    }
                }
            }
            catch (JsonException)
            {
                // Si falla el parsing JSON, intentar extraer manualmente algunos campos básicos
                return ExtractBasicModifications(response);
            }

            return modification;
        }

        private ModificationRequest ExtractBasicModifications(string response)
        {
            var modification = new ModificationRequest();
            var lowerResponse = response.ToLowerInvariant();

            // Intentar extraer hora básica
            var horaMatch = System.Text.RegularExpressions.Regex.Match(response, @"(\d{1,2}):(\d{2})");
            if (horaMatch.Success && TimeOnly.TryParseExact(horaMatch.Value, "HH:mm", out var hora))
            {
                modification.NewTime = hora;
            }

            // Intentar extraer fecha básica
            var fechaMatch = System.Text.RegularExpressions.Regex.Match(response, @"(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})");
            if (fechaMatch.Success && DateTime.TryParseExact(fechaMatch.Value, new[] { "dd/MM/yyyy", "dd-MM-yyyy" }, null, System.Globalization.DateTimeStyles.None, out var fecha))
            {
                modification.NewDate = fecha;
            }

            return modification;
        }

        public string GenerateModificationSummary(Appointment original, ModificationRequest changes)
        {
            var summary = "📝 *Cambios solicitados:*\n\n";

            if (changes.NewDate.HasValue)
            {
                var originalDate = original.FechaHora?.ToString("dd/MM/yyyy") ?? "No definida";
                summary += $"📅 Fecha: {originalDate} → *{changes.NewDate.Value:dd/MM/yyyy}*\n";
            }

            if (changes.NewTime.HasValue)
            {
                var originalTime = original.FechaHora?.ToString("HH:mm") ?? "No definida";
                summary += $"🕒 Hora: {originalTime} → *{changes.NewTime.Value:HH:mm}*\n";
            }

            if (!string.IsNullOrEmpty(changes.NewLocation))
            {
                summary += $"📍 Lugar: {original.Lugar ?? "No definido"} → *{changes.NewLocation}*\n";
            }

            if (!string.IsNullOrEmpty(changes.NewSurgeon))
            {
                summary += $"👨‍⚕️ Cirujano: {original.Cirujano ?? "No definido"} → *{changes.NewSurgeon}*\n";
            }

            if (!string.IsNullOrEmpty(changes.NewSurgeryType))
            {
                summary += $"🏥 Cirugía: {original.Cirugia ?? "No definida"} → *{changes.NewSurgeryType}*\n";
            }

            if (changes.NewQuantity.HasValue)
            {
                summary += $"🔢 Cantidad: {original.Cantidad} → *{changes.NewQuantity.Value}*\n";
            }

            if (!string.IsNullOrEmpty(changes.NewAnesthesiologist))
            {
                summary += $"💉 Anestesiólogo: {original.Anestesiologo ?? "No definido"} → *{changes.NewAnesthesiologist}*\n";
            }

            return summary;
        }
    }
}