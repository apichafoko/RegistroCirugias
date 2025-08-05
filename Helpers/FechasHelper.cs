using System;

namespace RegistroCx.Helpers;

public static class FechasHelper
{
    public static DateTime? ComposeFecha(string dia, string mes, string anio, string hora)
    {
        if (!int.TryParse(dia, out var d)) return null;
        if (!int.TryParse(mes, out var m)) return null;
        if (!int.TryParse(anio, out var y)) return null;

        // hora formato HH:MM
        if (!TimeSpan.TryParse(hora, out var ts)) return null;

        try
        {
            return new DateTime(y, m, d, ts.Hours, ts.Minutes, 0);
        }
        catch
        {
            return null;
        }
    }

    public static (bool ok, string? mensajeError) ValidarFechaCirugia(DateTime? fecha, DateTime ahora, int margenMin = 5)
    {
        if (fecha == null) return (true, null);

        var f = fecha.Value;
        f = new DateTime(f.Year, f.Month, f.Day, f.Hour, f.Minute, 0);

        if (f.Date < ahora.Date)
            return (false, $"La fecha interpretada es {f:dd/MM/yyyy HH:mm} y ya pasó. Reenvía el día/mes correcto.");

        if (f.Date == ahora.Date && f < ahora.AddMinutes(-margenMin))
            return (false, $"La hora {f:HH:mm} de hoy ya pasó. Confirmá o reenviá el dato.");

        if (f > ahora.AddMonths(18))
            return (false, $"La fecha {f:dd/MM/yyyy HH:mm} parece demasiado lejana. Confirmala.");

        return (true, null);
    }

}
