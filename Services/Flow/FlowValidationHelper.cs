using Telegram.Bot;
using RegistroCx.Models;
using RegistroCx.Helpers;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services.Flow;

public static class FlowValidationHelper
{
    public static async Task<bool> TryConfirmation(ITelegramBotClient bot, Appointment appt, long chatId, CancellationToken ct)
    {
        // El anestesiólogo ahora es opcional
        if (appt.FechaHora == null || appt.Lugar == null || appt.Cirujano == null ||
            appt.Cirugia == null || appt.Cantidad == null)
            return false;

        var (okFecha, err) = FechasHelper.ValidarFechaCirugia(appt.FechaHora, DateTime.Now);
        if (!okFecha)
        {
            await MessageSender.SendWithRetry(chatId, err!, cancellationToken: ct);
            return true;
        }

        var resumen = BuildConfirmationSummary(appt);
        appt.ConfirmacionPendiente = true;
        await MessageSender.SendWithRetry(chatId, resumen, cancellationToken: ct);
        return true;
    }

    public static async Task<bool> RequestMissingField(ITelegramBotClient bot, Appointment appt, long chatId, CancellationToken ct)
    {
        var faltantes = GetMissingFields(appt);
        Console.WriteLine($"[VALIDATION] Missing fields count: {faltantes.Count}");
        Console.WriteLine($"[VALIDATION] FechaHora={appt.FechaHora}, Lugar={appt.Lugar}, Cirujano={appt.Cirujano}, Cirugia={appt.Cirugia}, Cantidad={appt.Cantidad}, Anestesiologo={appt.Anestesiologo}");
        
        if (faltantes.Count == 0) 
        {
            Console.WriteLine("[VALIDATION] No missing fields - returning false");
            return false;
        }

        var primero = faltantes[0];
        Console.WriteLine($"[VALIDATION] Setting CampoQueFalta to: {primero}");
        appt.CampoQueFalta = primero;
        appt.IntentosCampoActual = 0;

        var mensaje = BuildMissingFieldMessage(appt, primero);
        await MessageSender.SendWithRetry(chatId, mensaje, cancellationToken: ct);
        return true;
    }

    private static string BuildConfirmationSummary(Appointment appt)
    {
        var anesthesiologistLine = string.IsNullOrEmpty(appt.Anestesiologo) ? 
               "• Anestesiólogo: (Sin asignar)\n" :
               $"• Anestesiólogo: {appt.Anestesiologo}\n";
               
        return "¿Confirmás estos datos?\n" +
               $"• Fecha y hora: {appt.FechaHora:dd/MM/yyyy HH:mm}\n" +
               $"• Lugar: {appt.Lugar}\n" +
               $"• Cirujano: {appt.Cirujano}\n" +
               $"• Cirugía: {appt.Cirugia}\n" +
               $"• Cantidad: {appt.Cantidad}\n" +
               anesthesiologistLine +
               "Respondé 'sí' / 'ok' para confirmar, o 'no' para corregir.";
    }

    private static List<Appointment.CampoPendiente> GetMissingFields(Appointment appt)
    {
        var faltantes = new List<Appointment.CampoPendiente>();

        if (appt.FechaHora == null)
        {
            faltantes.Add(Appointment.CampoPendiente.FechaHora);
        }
        if (appt.Lugar == null) faltantes.Add(Appointment.CampoPendiente.Lugar);
        if (appt.Cirujano == null) faltantes.Add(Appointment.CampoPendiente.Cirujano);
        if (appt.Cirugia == null) faltantes.Add(Appointment.CampoPendiente.Cirugia);
        if (appt.Cantidad == null) faltantes.Add(Appointment.CampoPendiente.Cantidad);
        if (appt.Anestesiologo == null) faltantes.Add(Appointment.CampoPendiente.PreguntandoSiAsignarAnestesiologo);

        return faltantes;
    }

    private static string BuildMissingFieldMessage(Appointment appt, Appointment.CampoPendiente campo)
    {
        if (campo == Appointment.CampoPendiente.FechaHora)
        {
            if (appt.TieneFechaPeroNoHora())
            {
                var fechaStr = $"{appt.DiaExtraido:D2}/{appt.MesExtraido:D2}";
                return $"Tengo la fecha ({fechaStr}) pero me falta la hora. ¿A qué hora? (ej: 14hs, 16:30)";
            }
            else
            {
                return "Me falta la fecha y hora. Indicá el valor (ej: 08/08 14hs, mañana 16:00).";
            }
        }

        if (campo == Appointment.CampoPendiente.PreguntandoSiAsignarAnestesiologo)
        {
            return "¿Querés asignar un anestesiólogo a esta cirugía? Respondé 'sí' o 'no'.";
        }
        
        if (campo == Appointment.CampoPendiente.SeleccionandoAnestesiologoCandidato)
        {
            return "Seleccioná el número del anestesiólogo que correspondá.";
        }

        return $"Me falta {CamposExistentes.NombreHumanoCampo(campo)}. Indicá el valor.";
    }
}