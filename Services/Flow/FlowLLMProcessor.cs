using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Helpers;
using RegistroCx.Services.Extraction;

namespace RegistroCx.Services.Flow;

public class FlowLLMProcessor
{
    private readonly LLMOpenAIAssistant _llm;

    public FlowLLMProcessor(LLMOpenAIAssistant llm)
    {
        _llm = llm;
    }

    public async Task ProcessWithLLM(ITelegramBotClient bot, Appointment appt, string rawText, long chatId, CancellationToken ct)
    {
        // Usar el context manager inteligente
        var (textoParaLLM, tipoOperacion) = LLMContextManager.CrearContextoInteligente(appt, rawText, DateTime.Today);

        // Extracción con LLM
        var dict = await _llm.ExtractWithPublishedPromptAsync(textoParaLLM, DateTime.Today);

        // Procesar respuesta según el tipo
        if (tipoOperacion == LLMContextManager.TipoOperacion.NormalizarCampo)
        {
            await HandleNormalization(bot, appt, dict, rawText, chatId, ct);
        }
        else
        {
            await HandleStandardProcessing(bot, appt, dict, chatId, ct);
        }
    }

    private async Task HandleNormalization(ITelegramBotClient bot, Appointment appt, 
        Dictionary<string, string> dict, string inputOriginal, long chatId, CancellationToken ct)
    {
        var (wasUpdated, fieldUpdated, newValue) = ApplyNormalization(appt, dict);

        if (wasUpdated && fieldUpdated != Appointment.CampoPendiente.Ninguno)
        {
            await bot.SendMessage(chatId,
                CamposExistentes.GenerarMensajeActualizacion(fieldUpdated, newValue),
                cancellationToken: ct);

            // Intentar confirmar o pedir siguiente campo
            if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct)) return;
            await FlowValidationHelper.RequestMissingField(bot, appt, chatId, ct);
        }
        else
        {
            await bot.SendMessage(chatId,
                $"No pude interpretar '{inputOriginal}'. ¿Podés ser más específico?",
                cancellationToken: ct);
        }
    }

    private async Task HandleStandardProcessing(ITelegramBotClient bot, Appointment appt, 
        Dictionary<string, string> dict, long chatId, CancellationToken ct)
    {
        ApplyAllFields(appt, dict);

        // Flujo normal: pedir faltantes o confirmar
        if (await FlowValidationHelper.RequestMissingField(bot, appt, chatId, ct)) return;
        if (await FlowValidationHelper.TryConfirmation(bot, appt, chatId, ct)) return;

        await bot.SendMessage(chatId,
            "Enviá los datos que faltan (fecha/hora, lugar, cirujano, cirugía, cantidad, anestesiólogo).",
            cancellationToken: ct);
    }

    private (bool wasUpdated, Appointment.CampoPendiente fieldUpdated, string newValue) ApplyNormalization(
        Appointment appt, Dictionary<string, string> dict)
    {
        // Detectar qué campo se normalizó
        foreach (var (key, value) in dict)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            switch (key)
            {
                case "lugar":
                    var oldLugar = appt.Lugar;
                    appt.Lugar = Capitalizador.CapitalizarSimple(value);
                    if (oldLugar != appt.Lugar)
                        return (true, Appointment.CampoPendiente.Lugar, appt.Lugar);
                    break;

                case "cirujano":
                    var oldCirujano = appt.Cirujano;
                    appt.Cirujano = Capitalizador.CapitalizarSimple(value);
                    if (oldCirujano != appt.Cirujano)
                        return (true, Appointment.CampoPendiente.Cirujano, appt.Cirujano);
                    break;

                case "anestesiologo":
                    var oldAnest = appt.Anestesiologo;
                    appt.Anestesiologo = Capitalizador.CapitalizarSimple(value);
                    if (oldAnest != appt.Anestesiologo)
                        return (true, Appointment.CampoPendiente.Anestesiologo, appt.Anestesiologo);
                    break;

                case "cirugía":
                    var oldCirugia = appt.Cirugia;
                    appt.Cirugia = Capitalizador.CapitalizarSimple(value);
                    if (oldCirugia != appt.Cirugia)
                        return (true, Appointment.CampoPendiente.Cirugia, appt.Cirugia);
                    break;
            }
        }

        return (false, Appointment.CampoPendiente.Ninguno, "");
    }

    private void ApplyAllFields(Appointment appt, Dictionary<string, string> dict)
    {
        if (dict.TryGetValue("lugar", out var lugar) && !string.IsNullOrWhiteSpace(lugar))
            appt.Lugar = Capitalizador.CapitalizarSimple(lugar);
        
        if (dict.TryGetValue("cirujano", out var cirujano) && !string.IsNullOrWhiteSpace(cirujano))
            appt.Cirujano = Capitalizador.CapitalizarSimple(cirujano);
        
        if (dict.TryGetValue("cirugía", out var cirugia) && !string.IsNullOrWhiteSpace(cirugia))
            appt.Cirugia = Capitalizador.CapitalizarSimple(cirugia);
        
        if (dict.TryGetValue("cantidad", out var q) && int.TryParse(q, out var n))
            appt.Cantidad = n;
        
        if (dict.TryGetValue("anestesiologo", out var anest) && !string.IsNullOrWhiteSpace(anest))
            appt.Anestesiologo = Capitalizador.CapitalizarSimple(anest);

        // Para fecha/hora
        TryParseDateTimeFromLLM(dict, appt);
    }

    private static void TryParseDateTimeFromLLM(Dictionary<string, string> dict, Appointment appt)
    {
        // Extraer valores del LLM
        if (dict.TryGetValue("dia", out var d) && int.TryParse(d, out var diaVal))
            appt.DiaExtraido = diaVal;
        
        if (dict.TryGetValue("mes", out var m) && int.TryParse(m, out var mesVal))
            appt.MesExtraido = mesVal;
        
        if (dict.TryGetValue("anio", out var y) && int.TryParse(y, out var anioVal))
            appt.AnioExtraido = anioVal;
        
        if (dict.TryGetValue("hora", out var h))
        {
            var horaText = h.Trim().ToLower().Replace("hs", "").Replace("h", "").Trim();
            if (TimeSpan.TryParse(horaText, out var horaSpan))
            {
                appt.HoraExtraida = horaSpan.Hours;
                appt.MinutoExtraido = horaSpan.Minutes;
            }
            else if (int.TryParse(horaText, out var horaInt) && horaInt >= 0 && horaInt <= 23)
            {
                appt.HoraExtraida = horaInt;
                appt.MinutoExtraido = 0;
            }
        }
        
        if (dict.TryGetValue("minuto", out var min) && int.TryParse(min, out var minVal))
            appt.MinutoExtraido = minVal;

        // Intentar completar la fecha/hora
        appt.TryCompletarFechaHora();
    }
}