using System;
namespace RegistroCx.Models;

public class Appointment
{
    public long ChatId { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Lugar { get; set; }
    public string? Cirujano { get; set; }
    public string? Cirugia { get; set; }
    public int? Cantidad { get; set; }
    public string? Anestesiologo { get; set; }
    public string? Notas { get; set; }
    public bool ConfirmacionPendiente { get; set; }
    
    public enum CampoPendiente
    {
        Ninguno,
        FechaHora,
        Lugar,
        Cirujano,
        Cantidad,
        Anestesiologo,
        Cirugia,
        EsperandoNombreCampo
    }
    
    public CampoPendiente CampoQueFalta { get; set; } = CampoPendiente.Ninguno;
    public CampoPendiente CampoAEditar { get; set; } = CampoPendiente.Ninguno;
    public int IntentosCampoActual { get; set; } = 0;
    public const int MaxIntentosCampo = 3;
    public List<string> HistoricoInputs { get; set; } = new();

    // Propiedad para compatibilidad
    public DateTime? FechaHora 
    { 
        get => Fecha; 
        set => Fecha = value; 
    }

    // NUEVOS campos para información parcial de fecha/hora
    public int? DiaExtraido { get; set; }
    public int? MesExtraido { get; set; }
    public int? AnioExtraido { get; set; }
    public int? HoraExtraida { get; set; }
    public int? MinutoExtraido { get; set; }

    // Método helper para construir DateTime completo cuando esté disponible
    public bool TryCompletarFechaHora()
    {
        if (DiaExtraido.HasValue && MesExtraido.HasValue && HoraExtraida.HasValue)
        {
            try
            {
                var anio = AnioExtraido ?? DateTime.Now.Year;
                var minuto = MinutoExtraido ?? 0;
                
                Fecha = new DateTime(anio, MesExtraido.Value, DiaExtraido.Value, HoraExtraida.Value, minuto, 0);
                
                // Limpiar campos parciales una vez que se completa
                DiaExtraido = null;
                MesExtraido = null; 
                AnioExtraido = null;
                HoraExtraida = null;
                MinutoExtraido = null;
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    // Método para verificar si falta solo la hora
    public bool TieneFechaPeroNoHora()
    {
        return DiaExtraido.HasValue && MesExtraido.HasValue && !HoraExtraida.HasValue;
    }

    // Método para obtener fecha base para cambios granulares
    public DateTime ObtenerFechaBase()
    {
        if (Fecha.HasValue)
            return Fecha.Value;
        
        if (DiaExtraido.HasValue && MesExtraido.HasValue)
        {
            var anio = AnioExtraido ?? DateTime.Now.Year;
            try
            {
                return new DateTime(anio, MesExtraido.Value, DiaExtraido.Value, 9, 0, 0);
            }
            catch
            {
                return DateTime.Now;
            }
        }
        
        return DateTime.Now;
    }
}