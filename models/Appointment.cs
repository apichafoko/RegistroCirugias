using System;
using RegistroCx.models;
namespace RegistroCx.Models;

public class Appointment
{
    public long Id { get; set; }
    public long? ChatId { get; set; } // Mantener para compatibilidad temporal
    public int? EquipoId { get; set; } // Nueva relación con equipos
    public int? UserProfileId { get; set; } // Relación con user_profiles
    public string? GoogleEmail { get; set; } // Para reportes compartidos por equipo
    public DateTime? Fecha { get; set; }
    public string? Lugar { get; set; }
    public string? Cirujano { get; set; }
    public string? Cirugia { get; set; }
    public int? Cantidad { get; set; }
    public string? Anestesiologo { get; set; }
    public string? Notas { get; set; }
    public bool ConfirmacionPendiente { get; set; }
    
    // Para tracking de sincronización con Google Calendar
    public string? CalendarEventId { get; set; }
    public DateTime? CalendarSyncedAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
    
    public enum CampoPendiente
    {
        Ninguno,
        FechaHora,
        Lugar,
        Cirujano,
        Cantidad,
        PreguntandoSiAsignarAnestesiologo,
        SeleccionandoAnestesiologoCandidato,
        Anestesiologo,
        Cirugia,
        EsperandoNombreCampo,
        EsperandoEmailAnestesiologo
    }
    
    public CampoPendiente CampoQueFalta { get; set; } = CampoPendiente.Ninguno;
    public CampoPendiente CampoAEditar { get; set; } = CampoPendiente.Ninguno;
    public int IntentosCampoActual { get; set; } = 0;
    public const int MaxIntentosCampo = 3;
    public List<string> HistoricoInputs { get; set; } = new();

    // Campos para manejo de email de anestesiólogo
    public string? PendingEventId { get; set; }
    public string? PendingAnesthesiologistName { get; set; }
    public bool ReadyForCleanup { get; set; } = false;

    // Campo para almacenar candidatos de anestesiólogo durante búsqueda
    public List<string> AnesthesiologistCandidates { get; set; } = new();
    
    // Campo para manejar warnings de validación que permiten continuar
    public string? ValidationWarning { get; set; }
    
    // Campo para contexto de modificación
    public ModificationContext? ModificationContext { get; set; }

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

public class ModificationContext
{
    public Appointment? OriginalAppointment { get; set; }
    public ModificationRequest? RequestedChanges { get; set; }
    public Appointment? ModifiedAppointment { get; set; } // Nueva propiedad para el appointment modificado
    public bool IsAwaitingConfirmation { get; set; }
}