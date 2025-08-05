using System;
using System.Collections.Generic;
using System.Text.Json;
using RegistroCx.Models;

namespace RegistroCx.Services.Extraction;

public static class LLMContextManager
{
    public static class TipoOperacion
    {
        public const string NuevoCaso = "nuevo_caso";
        public const string NormalizarCampo = "normalizar_campo";
        public const string CompletarFaltantes = "completar_faltantes";
    }

    public static (string textoParaLLM, string tipoOperacion) CrearContextoInteligente(
        Appointment appt, 
        string inputUsuario, 
        DateTime fechaReferencia)
    {
        // Detectar el tipo de operación
        var cambioEspecifico = DetectarCambioEspecifico(inputUsuario);
        
        if (cambioEspecifico.HasValue)
        {
            // Es un cambio específico - usar contexto estructurado
            return CrearContextoNormalizacion(appt, inputUsuario, cambioEspecifico.Value, fechaReferencia);
        }
        else if (TieneAlgunDato(appt))
        {
            // Tiene datos parciales - completar faltantes
            return CrearContextoCompletar(appt, inputUsuario, fechaReferencia);
        }
        else
        {
            // Caso nuevo - procesar normal
            return CrearContextoNuevo(inputUsuario, fechaReferencia);
        }
    }

    private static (string, string) CrearContextoNormalizacion(
        Appointment appt,
        string inputUsuario, 
        Appointment.CampoPendiente campo,
        DateTime fechaReferencia)
    {
        var valorUsuario = ExtraerValorDelInput(inputUsuario, campo);
        
        var contexto = new
        {
            operacion = TipoOperacion.NormalizarCampo,
            campo = CampoAString(campo),
            valor_usuario = valorUsuario,
            contexto_actual = CrearContextoActual(appt),
            fecha_hoy = fechaReferencia.ToString("dd/MM/yyyy")
        };

        var textoParaLLM = JsonSerializer.Serialize(contexto, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return (textoParaLLM, TipoOperacion.NormalizarCampo);
    }

    private static (string, string) CrearContextoCompletar(
        Appointment appt,
        string inputUsuario,
        DateTime fechaReferencia)
    {
        var contexto = new
        {
            operacion = TipoOperacion.CompletarFaltantes,
            nuevo_input = inputUsuario,
            datos_existentes = CrearContextoActual(appt),
            fecha_hoy = fechaReferencia.ToString("dd/MM/yyyy")
        };

        var textoParaLLM = JsonSerializer.Serialize(contexto, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return (textoParaLLM, TipoOperacion.CompletarFaltantes);
    }

    private static (string, string) CrearContextoNuevo(string inputUsuario, DateTime fechaReferencia)
    {
        var contexto = new
        {
            operacion = TipoOperacion.NuevoCaso,
            texto = inputUsuario,
            fecha_hoy = fechaReferencia.ToString("dd/MM/yyyy")
        };

        // Para casos nuevos, podemos usar el formato anterior o el nuevo
        // Por simplicidad, mantenemos el formato original para casos nuevos
        return ($"{inputUsuario}\n\nFECHA_HOY={fechaReferencia:dd/MM/yyyy}", TipoOperacion.NuevoCaso);
    }

    private static object CrearContextoActual(Appointment appt)
    {
        var contexto = new Dictionary<string, object?>();

        if (appt.FechaHora.HasValue)
        {
            contexto["dia"] = appt.FechaHora.Value.Day.ToString("D2");
            contexto["mes"] = appt.FechaHora.Value.Month.ToString("D2");
            contexto["anio"] = appt.FechaHora.Value.Year.ToString();
            contexto["hora"] = appt.FechaHora.Value.ToString("HH:mm");
        }
        else
        {
            // Incluir información parcial si existe
            if (appt.DiaExtraido.HasValue)
                contexto["dia"] = appt.DiaExtraido.Value.ToString("D2");
            if (appt.MesExtraido.HasValue)
                contexto["mes"] = appt.MesExtraido.Value.ToString("D2");
            if (appt.AnioExtraido.HasValue)
                contexto["anio"] = appt.AnioExtraido.Value.ToString();
            if (appt.HoraExtraida.HasValue)
                contexto["hora"] = $"{appt.HoraExtraida:D2}:{appt.MinutoExtraido ?? 0:D2}";
        }

        if (!string.IsNullOrWhiteSpace(appt.Lugar))
            contexto["lugar"] = appt.Lugar;
        if (!string.IsNullOrWhiteSpace(appt.Cirujano))
            contexto["cirujano"] = appt.Cirujano;
        if (!string.IsNullOrWhiteSpace(appt.Cirugia))
            contexto["cirugia"] = appt.Cirugia;
        if (appt.Cantidad.HasValue)
            contexto["cantidad"] = appt.Cantidad.ToString();
        if (!string.IsNullOrWhiteSpace(appt.Anestesiologo))
            contexto["anestesiologo"] = appt.Anestesiologo;

        return contexto;
    }

    private static Appointment.CampoPendiente? DetectarCambioEspecifico(string input)
    {
        var inputLower = input.Trim().ToLowerInvariant();

        // Patrones para detectar cambios específicos
        if (inputLower.StartsWith("lugar ") || inputLower.StartsWith("hospital ") || inputLower.StartsWith("clinica "))
            return Appointment.CampoPendiente.Lugar;
        
        if (inputLower.StartsWith("cirujano ") || inputLower.StartsWith("doctor ") || inputLower.StartsWith("dr "))
            return Appointment.CampoPendiente.Cirujano;
        
        if (inputLower.StartsWith("anestesiologo ") || inputLower.StartsWith("anest ") || inputLower.StartsWith("anestesia "))
            return Appointment.CampoPendiente.Anestesiologo;
        
        if (inputLower.StartsWith("cirugia ") || inputLower.StartsWith("cx ") || inputLower.StartsWith("operacion "))
            return Appointment.CampoPendiente.Cirugia;
        
        if (inputLower.StartsWith("cantidad ") || inputLower.StartsWith("cant ") || inputLower.StartsWith("num "))
            return Appointment.CampoPendiente.Cantidad;

        // MEJORADO: Detectar si es solo hora (incluye punto como separador)
        if (System.Text.RegularExpressions.Regex.IsMatch(inputLower, @"^\d{1,2}(:\d{2}|\.?\d{2})?hs?$"))
        {
            // Es solo hora, pero este caso se maneja como completar faltantes o cambio de fecha/hora
            return null;
        }

        // Detectar si es solo un valor simple que necesita normalización
        var palabras = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (palabras.Length <= 3 && !EsFechaHora(input) && !EsSoloNumero(input))
        {
            // Probablemente sea un alias que necesita normalización
            // En este caso, no podemos saber qué campo es, así que devolvemos null
            // y será manejado como completar faltantes
            return null;
        }

        return null;
    }

    private static string ExtraerValorDelInput(string input, Appointment.CampoPendiente campo)
    {
        var inputLower = input.Trim().ToLowerInvariant();
        
        var prefijos = campo switch
        {
            Appointment.CampoPendiente.Lugar => new[] { "lugar ", "hospital ", "clinica " },
            Appointment.CampoPendiente.Cirujano => new[] { "cirujano ", "doctor ", "dr " },
            Appointment.CampoPendiente.Anestesiologo => new[] { "anestesiologo ", "anest ", "anestesia " },
            Appointment.CampoPendiente.Cirugia => new[] { "cirugia ", "cx ", "operacion " },
            Appointment.CampoPendiente.Cantidad => new[] { "cantidad ", "cant ", "num " },
            _ => new string[0]
        };

        foreach (var prefijo in prefijos)
        {
            if (inputLower.StartsWith(prefijo))
            {
                return input.Substring(prefijo.Length).Trim();
            }
        }

        return input.Trim();
    }

    private static string CampoAString(Appointment.CampoPendiente campo)
    {
        return campo switch
        {
            Appointment.CampoPendiente.Lugar => "lugar",
            Appointment.CampoPendiente.Cirujano => "cirujano",
            Appointment.CampoPendiente.Anestesiologo => "anestesiologo",
            Appointment.CampoPendiente.Cirugia => "cirugia",
            Appointment.CampoPendiente.Cantidad => "cantidad",
            _ => "desconocido"
        };
    }

    private static bool TieneAlgunDato(Appointment appt)
    {
        return appt.FechaHora.HasValue ||
               appt.DiaExtraido.HasValue ||
               !string.IsNullOrWhiteSpace(appt.Lugar) ||
               !string.IsNullOrWhiteSpace(appt.Cirujano) ||
               !string.IsNullOrWhiteSpace(appt.Cirugia) ||
               appt.Cantidad.HasValue ||
               !string.IsNullOrWhiteSpace(appt.Anestesiologo);
    }

    private static bool EsFechaHora(string input)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(input, @"(\d{1,2}[\/\-]\d{1,2})|(\d{1,2}[:\.]?\d{2})|(\d{1,2}hs?)");
    }

    private static bool EsSoloNumero(string input)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(input.Trim(), @"^\d+$");
    }
}