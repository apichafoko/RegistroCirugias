using System;

namespace RegistroCx.Helpers;

public class Capitalizador
{
public static string CapitalizarSimple(string s)
{
    if (string.IsNullOrWhiteSpace(s)) return s;
    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (int i = 0; i < parts.Length; i++)
    {
        var p = parts[i];
        if (p.Length == 1)
            parts[i] = p.ToUpperInvariant();
        else
            parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
    }
    return string.Join(' ', parts);
}
}
