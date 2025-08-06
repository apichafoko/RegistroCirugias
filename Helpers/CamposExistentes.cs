using System;
using System.Text.RegularExpressions;
using RegistroCx.Models;

namespace RegistroCx.Helpers;

public static class CamposExistentes
{
    // TU MÉTODO EXISTENTE (mantenido igual)
    public static bool TryParseCambioCampo(
        string input,
        out Appointment.CampoPendiente campo,
        out string? valor)
    {
        campo = Appointment.CampoPendiente.Ninguno;
        valor = null;

        if (string.IsNullOrWhiteSpace(input)) return false;

        var txt = input.Trim().ToLowerInvariant();

        // Eliminar verbos de acción al inicio
        txt = Regex.Replace(
            txt,
            @"^(cambiar|cambio|corregir|corregi|corregir|modificar|modifico|modifica)\s+",
            "",
            RegexOptions.IgnoreCase);

        // Normalizar acentos clave
        txt = txt
            .Replace("á", "a")
            .Replace("é", "e")
            .Replace("í", "i")
            .Replace("ó", "o")
            .Replace("ú", "u");

        var parts = txt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        string primera = parts[0];

        Appointment.CampoPendiente detectado = primera switch
        {
            "fecha" or "fechahora" or "fechayhora" or "hora" => Appointment.CampoPendiente.FechaHora,
            "lugar" => Appointment.CampoPendiente.Lugar,
            "cirujano" => Appointment.CampoPendiente.Cirujano,
            "cirugia" or "cirugía" or "cx" => Appointment.CampoPendiente.Cirugia,
            "cantidad" or "cant" or "num" => Appointment.CampoPendiente.Cantidad,
            "anestesiologo" or "anestesio" or "anestesiologo" or "anest" => Appointment.CampoPendiente.Anestesiologo,
            _ => Appointment.CampoPendiente.Ninguno
        };

        if (detectado == Appointment.CampoPendiente.Ninguno)
            return false;

        campo = detectado;

        if (parts.Length > 1)
        {
            valor = string.Join(' ', parts[1..]).Trim();
            if (string.IsNullOrWhiteSpace(valor))
                valor = null;
        }

        return true;
    }

    // NUEVO: Método para parsear solo el nombre del campo (sin valor)
    public static bool TryParseSoloCampo(string input, out Appointment.CampoPendiente campo)
    {
        campo = Appointment.CampoPendiente.Ninguno;

        if (string.IsNullOrWhiteSpace(input)) return false;

        var txt = input.Trim().ToLowerInvariant();

        // Normalizar acentos clave
        txt = txt
            .Replace("á", "a")
            .Replace("é", "e")
            .Replace("í", "i")
            .Replace("ó", "o")
            .Replace("ú", "u");

        // Buscar solo la palabra, sin espacios adicionales
        campo = txt switch
        {
            "fecha" or "fechahora" or "fechayhora" or "hora" => Appointment.CampoPendiente.FechaHora,
            "lugar" => Appointment.CampoPendiente.Lugar,
            "cirujano" => Appointment.CampoPendiente.Cirujano,
            "cirugia" or "cirugía" or "cx" => Appointment.CampoPendiente.Cirugia,
            "cantidad" or "cant" or "num" => Appointment.CampoPendiente.Cantidad,
            "anestesiologo" or "anestesio" or "anest" => Appointment.CampoPendiente.Anestesiologo,
            _ => Appointment.CampoPendiente.Ninguno
        };

        return campo != Appointment.CampoPendiente.Ninguno;
    }

    // MEJORADO: Método con parser inteligente de fecha/hora
    public static bool TryAplicarValorCampo(
        Appointment appt,
        Appointment.CampoPendiente campo,
        string valor,
        out string? error)
    {
        error = null;
        valor = valor.Trim();
        switch (campo)
        {
            case Appointment.CampoPendiente.Lugar:
                if (valor.Length < 2) { error = "Lugar demasiado corto."; return false; }
                appt.Lugar = Capitalizador.CapitalizarSimple(valor);
                return true;

            case Appointment.CampoPendiente.Cirujano:
                if (valor.Length < 2) { error = "Nombre muy corto."; return false; }
                appt.Cirujano = Capitalizador.CapitalizarSimple(valor);
                return true;

            case Appointment.CampoPendiente.Anestesiologo:
                if (valor.Length < 2) { error = "Nombre muy corto."; return false; }
                appt.Anestesiologo = Capitalizador.CapitalizarSimple(valor);
                return true;

            case Appointment.CampoPendiente.Cirugia:
                if (valor.Length < 2) { error = "Nombre de la cirugía muy corto."; return false; }
                appt.Cirugia = Capitalizador.CapitalizarSimple(valor);
                return true;

            case Appointment.CampoPendiente.Cantidad:
                var mCant = Regex.Match(valor, @"\d+");
                if (!mCant.Success || !int.TryParse(mCant.Value, out var num) || num <= 0 || num > 100)
                {
                    error = "Necesito un número válido para cantidad.";
                    return false;
                }
                appt.Cantidad = num;
                return true;

            case Appointment.CampoPendiente.FechaHora:
                // NUEVO: Parser inteligente de fecha/hora
                return TryParsearFechaHoraInteligente(valor, appt, out error);

            default:
                error = "Campo no soportado.";
                return false;
        }
    }

    // NUEVO: Parser inteligente que maneja cambios granulares
    private static bool TryParsearFechaHoraInteligente(string valor, Appointment appt, out string? error)
    {
        error = null;
        var valorLower = valor.Trim().ToLowerInvariant();

        try
        {
            // Obtener fecha base (usar información parcial si está disponible)
            DateTime fechaBase = appt.ObtenerFechaBase();

            // CASO 1: Solo hora (ej: "16", "16:30", "16hs")
            if (TryParsearSoloHora(valor, out var nuevaHora, out var nuevosMinutos))
            {
                // Si tengo información parcial de fecha, usarla
                if (appt.DiaExtraido.HasValue && appt.MesExtraido.HasValue)
                {
                    var anio = appt.AnioExtraido ?? DateTime.Now.Year;
                    appt.FechaHora = new DateTime(anio, appt.MesExtraido.Value, appt.DiaExtraido.Value, nuevaHora, nuevosMinutos, 0);
                    
                    // Limpiar información parcial
                    appt.DiaExtraido = null;
                    appt.MesExtraido = null;
                    appt.AnioExtraido = null;
                    appt.HoraExtraida = null;
                    appt.MinutoExtraido = null;
                }
                else
                {
                    appt.FechaHora = new DateTime(
                        fechaBase.Year, fechaBase.Month, fechaBase.Day,
                        nuevaHora, nuevosMinutos, 0);
                }
                return true;
            }

            // CASO 2: Solo fecha (ej: "08/08", "8/8", "mañana")
            if (TryParsearSoloFecha(valor, fechaBase, out var nuevaFecha))
            {
                appt.FechaHora = new DateTime(
                    nuevaFecha.Year, nuevaFecha.Month, nuevaFecha.Day,
                    fechaBase.Hour, fechaBase.Minute, 0);
                return true;
            }

            // CASO 3: Fecha + Hora completa (ej: "08/08 16:00", "mañana 14hs")
            if (TryParsearFechaHoraCompleta(valor, out var fechaHoraCompleta))
            {
                appt.FechaHora = fechaHoraCompleta;
                return true;
            }

            // CASO 4: Comandos de fecha relativa (ej: "mañana", "pasado mañana")
            if (TryParsearFechaRelativa(valorLower, fechaBase, out var fechaRelativa))
            {
                appt.FechaHora = new DateTime(
                    fechaRelativa.Year, fechaRelativa.Month, fechaRelativa.Day,
                    fechaBase.Hour, fechaBase.Minute, 0);
                return true;
            }

            error = "Formato de fecha/hora no reconocido. Ejemplos: '16hs', '08/08', '08/08 16:00', 'mañana'";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Error procesando fecha/hora: {ex.Message}";
            return false;
        }
    }

    private static bool TryParsearSoloHora(string valor, out int hora, out int minutos)
    {
        hora = 0;
        minutos = 0;

        // Limpiar el texto
        var horaText = valor.Trim().ToLower()
            .Replace("hs", "").Replace("h", "").Trim();

        // Formato HH:mm o HH.mm
        if (horaText.Contains(":") || horaText.Contains("."))
        {
            var separador = horaText.Contains(":") ? ':' : '.';
            var partes = horaText.Split(separador);
            if (partes.Length == 2 &&
                int.TryParse(partes[0], out hora) &&
                int.TryParse(partes[1], out minutos) &&
                hora >= 0 && hora <= 23 && minutos >= 0 && minutos <= 59)
            {
                return true;
            }
        }

        // Formato solo hora (ej: "16")
        if (int.TryParse(horaText, out hora) && hora >= 0 && hora <= 23)
        {
            minutos = 0;
            return true;
        }

        // Formato HHMM (ej: "1630")
        if (horaText.Length == 4 && int.TryParse(horaText, out var horaCompleta))
        {
            hora = horaCompleta / 100;
            minutos = horaCompleta % 100;
            if (hora >= 0 && hora <= 23 && minutos >= 0 && minutos <= 59)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParsearSoloFecha(string valor, DateTime fechaBase, out DateTime nuevaFecha)
    {
        nuevaFecha = fechaBase;

        // Patrones de fecha dd/mm o dd-mm
        var patronesFecha = new[]
        {
            @"^(\d{1,2})[\/\-](\d{1,2})$",           // dd/mm
            @"^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})$" // dd/mm/yyyy
        };

        foreach (var patron in patronesFecha)
        {
            var match = Regex.Match(valor, patron);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var dia) &&
                    int.TryParse(match.Groups[2].Value, out var mes))
                {
                    if (dia >= 1 && dia <= 31 && mes >= 1 && mes <= 12)
                    {
                        var anio = fechaBase.Year;
                        
                        // Si hay año en el match
                        if (match.Groups.Count > 3 && int.TryParse(match.Groups[3].Value, out var anioVal))
                        {
                            anio = anioVal < 100 ? (anioVal < 50 ? 2000 + anioVal : 1900 + anioVal) : anioVal;
                        }

                        try
                        {
                            nuevaFecha = new DateTime(anio, mes, dia);
                            return true;
                        }
                        catch
                        {
                            // Fecha inválida
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool TryParsearFechaHoraCompleta(string valor, out DateTime fechaHora)
    {
        fechaHora = default;

        // Patrón: dd/mm HH:mm o dd/mm HHhs
        var patrones = new[]
        {
            @"(\d{1,2})[\/\-](\d{1,2})\s+(\d{1,2}):(\d{2})",     // dd/mm HH:mm
            @"(\d{1,2})[\/\-](\d{1,2})\s+(\d{1,2})hs?",          // dd/mm HHhs
            @"(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})\s+(\d{1,2}):?(\d{2})?", // dd/mm/yyyy HH:mm
        };

        foreach (var patron in patrones)
        {
            var match = Regex.Match(valor, patron);
            if (match.Success)
            {
                try
                {
                    var dia = int.Parse(match.Groups[1].Value);
                    var mes = int.Parse(match.Groups[2].Value);
                    var anio = DateTime.Now.Year;
                    var hora = int.Parse(match.Groups[3].Value);
                    var minuto = 0;

                    // Si hay año
                    if (match.Groups.Count > 5 && int.TryParse(match.Groups[4].Value, out var anioVal))
                    {
                        anio = anioVal < 100 ? (anioVal < 50 ? 2000 + anioVal : 1900 + anioVal) : anioVal;
                        if (match.Groups.Count > 6 && int.TryParse(match.Groups[5].Value, out var horaVal))
                            hora = horaVal;
                        if (match.Groups.Count > 7 && int.TryParse(match.Groups[6].Value, out var minVal))
                            minuto = minVal;
                    }
                    // Si hay minutos sin año
                    else if (match.Groups.Count > 4 && int.TryParse(match.Groups[4].Value, out var minutoVal))
                    {
                        minuto = minutoVal;
                    }

                    if (dia >= 1 && dia <= 31 && mes >= 1 && mes <= 12 && 
                        hora >= 0 && hora <= 23 && minuto >= 0 && minuto <= 59)
                    {
                        fechaHora = new DateTime(anio, mes, dia, hora, minuto, 0);
                        return true;
                    }
                }
                catch
                {
                    // Error de parsing, continuar con siguiente patrón
                }
            }
        }

        return false;
    }

    private static bool TryParsearFechaRelativa(string valor, DateTime fechaBase, out DateTime fechaRelativa)
    {
        fechaRelativa = fechaBase;

        switch (valor)
        {
            case "hoy":
                fechaRelativa = DateTime.Today;
                return true;
            case "mañana" or "mañana":
                fechaRelativa = DateTime.Today.AddDays(1);
                return true;
            case "pasado mañana" or "pasado manana":
                fechaRelativa = DateTime.Today.AddDays(2);
                return true;
            case "ayer":
                fechaRelativa = DateTime.Today.AddDays(-1);
                return true;
        }

        return false;
    }

    // TU MÉTODO EXISTENTE (mantenido igual)
    public static string NombreHumanoCampo(Appointment.CampoPendiente campo) => campo switch
    {
        Appointment.CampoPendiente.FechaHora => "fecha y hora",
        Appointment.CampoPendiente.Lugar => "lugar",
        Appointment.CampoPendiente.Cirujano => "cirujano",
        Appointment.CampoPendiente.Cirugia => "cirugía",
        Appointment.CampoPendiente.Cantidad => "cantidad",
        Appointment.CampoPendiente.Anestesiologo => "anestesiólogo",
        _ => "campo"
    };

    // NUEVO: Mensajes humanos para confirmaciones de cambios
    public static string GenerarMensajeActualizacion(Appointment.CampoPendiente campo, string valor)
    {
        var valorLimpio = valor.Trim();
        
        return campo switch
        {
            Appointment.CampoPendiente.FechaHora => GenerarMensajeFechaHora(valorLimpio),
            Appointment.CampoPendiente.Lugar => $"✅ Perfecto, cambié el lugar a {valorLimpio}.",
            Appointment.CampoPendiente.Cirujano => $"✅ Listo, el cirujano ahora es {valorLimpio}.",
            Appointment.CampoPendiente.Cirugia => $"✅ Actualicé la cirugía a {valorLimpio}.",
            Appointment.CampoPendiente.Cantidad => GenerarMensajeCantidad(valorLimpio),
            Appointment.CampoPendiente.Anestesiologo => $"✅ Perfecto, el anestesiólogo es {valorLimpio}.",
            _ => $"✅ Actualicé {NombreHumanoCampo(campo)} a: {valorLimpio}"
        };
    }

    private static string GenerarMensajeFechaHora(string valor)
    {
        var valorLower = valor.ToLowerInvariant().Trim();

        // Detectar si es solo hora
        if (EsSoloHora(valor))
        {
            var horaFormateada = FormatearHora(valor);
            return $"✅ Perfecto, cambié el horario a las {horaFormateada}.";
        }

        // Detectar si es solo fecha
        if (EsSoloFecha(valor))
        {
            var fechaFormateada = FormatearFecha(valor);
            return $"✅ Listo, cambié la fecha para el {fechaFormateada}.";
        }

        // Fecha relativa
        if (valorLower.Contains("mañana"))
        {
            return "✅ Perfecto, lo programé para mañana.";
        }
        if (valorLower.Contains("hoy"))
        {
            return "✅ Listo, queda para hoy.";
        }
        if (valorLower.Contains("pasado"))
        {
            return "✅ Programado para pasado mañana.";
        }

        // Fecha + hora completa
        return $"✅ Actualicé la fecha y hora a {valor}.";
    }

    private static string GenerarMensajeCantidad(string valor)
    {
        if (int.TryParse(valor, out var num))
        {
            return num switch
            {
                1 => "✅ Listo, es una sola cirugía.",
                2 => "✅ Perfecto, son 2 cirugías.",
                3 => "✅ Anotado, son 3 cirugías.",
                _ => $"✅ Actualicé la cantidad a {num} cirugías."
            };
        }
        return $"✅ Cambié la cantidad a {valor}.";
    }

    private static bool EsSoloHora(string valor)
    {
        var valorLimpio = valor.ToLower().Replace("hs", "").Replace("h", "").Trim();
        
        // Formatos: "14", "14:30", "1430"
        return System.Text.RegularExpressions.Regex.IsMatch(valorLimpio, @"^\d{1,2}(:\d{2})?$") ||
               System.Text.RegularExpressions.Regex.IsMatch(valorLimpio, @"^\d{4}$");
    }

    private static bool EsSoloFecha(string valor)
    {
        // Formatos: "07/08", "7/8", "07-08"
        return System.Text.RegularExpressions.Regex.IsMatch(valor, @"^\d{1,2}[\/\-]\d{1,2}(\d{2,4})?$");
    }

    private static string FormatearHora(string valor)
    {
        var valorLimpio = valor.ToLower().Replace("hs", "").Replace("h", "").Trim();
        
        if (valorLimpio.Contains(":"))
        {
            return valorLimpio + "hs";
        }
        
        if (int.TryParse(valorLimpio, out var hora))
        {
            if (valorLimpio.Length == 4) // Formato HHMM
            {
                var h = hora / 100;
                var m = hora % 100;
                return m == 0 ? $"{h}hs" : $"{h:D2}:{m:D2}hs";
            }
            else // Formato H o HH
            {
                return $"{hora}hs";
            }
        }
        
        return valor + "hs";
    }

    private static string FormatearFecha(string valor)
    {
        // Intentar formatear fecha de manera más natural
        var match = System.Text.RegularExpressions.Regex.Match(valor, @"(\d{1,2})[\/\-](\d{1,2})");
        if (match.Success)
        {
            var dia = int.Parse(match.Groups[1].Value);
            var mes = int.Parse(match.Groups[2].Value);
            
            var nombreMes = mes switch
            {
                1 => "enero", 2 => "febrero", 3 => "marzo", 4 => "abril",
                5 => "mayo", 6 => "junio", 7 => "julio", 8 => "agosto",
                9 => "septiembre", 10 => "octubre", 11 => "noviembre", 12 => "diciembre",
                _ => $"mes {mes}"
            };
            
            return $"{dia} de {nombreMes}";
        }
        
        return valor;
    }
}