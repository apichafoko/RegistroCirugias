using System.Text.RegularExpressions;

namespace RegistroCx.Services.Helpers;

public static class FechasHelper
{
    public static DateTime? ComposeFecha(string dia, string mes, string anio, string hora, string minuto)
    {
        if (!int.TryParse(dia, out var d)) return null;
        if (!int.TryParse(mes, out var m)) return null;
        if (!int.TryParse(anio, out var y)) return null;
        if (!int.TryParse(hora, out var hh)) return null;
        if (!int.TryParse(minuto, out var mm)) return null;
        try { return new DateTime(y, m, d, hh, mm, 0); }
        catch { return null; }
    }

    public static (bool ok, string? error) ValidarFechaCirugia(DateTime? fecha, DateTime now)
    {
        if (fecha == null) return (false, "Fecha inválida.");
        if (fecha <= now) return (false, "La fecha/hora ya pasó. Reenviá una futura.");
        return (true, null);
    }
}
