using System;

namespace RegistroCx.Services.Helpers;

public static class Capitalizador
{
 public static string CapitalizarSimple(string s) =>
        string.Join(" ", s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLower() : "")));
}
