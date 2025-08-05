using System;
using RegistroCx.Helpers.OpenAI;

namespace RegistroCx.Helpers;

public static class ReferenciasHelper
{
    public static string? GetAnestesiologoEmail(string nombreNormalizado, ListasReferencia listas)
    {
        if (string.IsNullOrWhiteSpace(nombreNormalizado)) return null;
        nombreNormalizado = nombreNormalizado.Trim();

        foreach (var item in listas.Listas.Anestesiologos)
        {
            if (EsMatch(item, nombreNormalizado)) return item.Email;
        }
        return null;
    }

    public static string? GetCirujanoEmail(string nombreNormalizado, ListasReferencia listas)
    {
        if (string.IsNullOrWhiteSpace(nombreNormalizado)) return null;
        nombreNormalizado = nombreNormalizado.Trim();

        foreach (var item in listas.Listas.Cirujanos)
        {
            if (EsMatch(item, nombreNormalizado)) return item.Email;
        }
        return null;
    }

    private static bool EsMatch(ListaItem li, string valor) =>
        li.Nombre.Equals(valor, StringComparison.OrdinalIgnoreCase) ||
        (li.Alias?.Any(a => a.Equals(valor, StringComparison.OrdinalIgnoreCase)) ?? false);
}
