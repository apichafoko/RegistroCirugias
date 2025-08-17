using System;
using System.Collections.Generic;

namespace RegistroCx.Domain
{
    public class Equipo
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public EstadoSuscripcion EstadoSuscripcion { get; set; } = EstadoSuscripcion.Prueba;
        public DateTime? FechaPago { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public EstadoEquipo Estado { get; set; } = EstadoEquipo.Activo;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Propiedades calculadas
        public bool EsSuscripcionValida => 
            EstadoSuscripcion == EstadoSuscripcion.Pago && 
            Estado == EstadoEquipo.Activo &&
            (FechaVencimiento == null || FechaVencimiento > DateTime.UtcNow);

        public bool EstaEnPeriodoPrueba => EstadoSuscripcion == EstadoSuscripcion.Prueba;

        public int DiasRestantesSuscripcion
        {
            get
            {
                if (FechaVencimiento == null) return int.MaxValue;
                var dias = (FechaVencimiento.Value - DateTime.UtcNow).Days;
                return Math.Max(0, dias);
            }
        }

        public bool RequiereRenovacion => 
            DiasRestantesSuscripcion <= 7 && 
            EstadoSuscripcion == EstadoSuscripcion.Pago;

        // Métodos de utilidad
        public void ActivarSuscripcion(DateTime fechaPago, DateTime fechaVencimiento)
        {
            EstadoSuscripcion = EstadoSuscripcion.Pago;
            FechaPago = fechaPago;
            FechaVencimiento = fechaVencimiento;
            Estado = EstadoEquipo.Activo;
            UpdatedAt = DateTime.UtcNow;
        }

        public void CancelarSuscripcion()
        {
            EstadoSuscripcion = EstadoSuscripcion.Cancelado;
            UpdatedAt = DateTime.UtcNow;
        }

        public void DesactivarEquipo()
        {
            Estado = EstadoEquipo.Inactivo;
            UpdatedAt = DateTime.UtcNow;
        }

        public void RenovarSuscripcion(DateTime nuevaFechaVencimiento)
        {
            FechaVencimiento = nuevaFechaVencimiento;
            FechaPago = DateTime.UtcNow;
            EstadoSuscripcion = EstadoSuscripcion.Pago;
            Estado = EstadoEquipo.Activo;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public enum EstadoSuscripcion
    {
        Prueba,
        Pago,
        Cancelado,
        Vencido
    }

    public enum EstadoEquipo
    {
        Activo,
        Inactivo
    }

    public class UserProfileEquipo
    {
        public int Id { get; set; }
        public int UserProfileId { get; set; }
        public int EquipoId { get; set; }
        public RolEquipo Rol { get; set; } = RolEquipo.Miembro;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Propiedades de navegación
        public UserProfile? UserProfile { get; set; }
        public Equipo? Equipo { get; set; }
    }

    [Obsolete("UsuarioTelegramEquipo ha sido migrado a UserProfileEquipo. Use UserProfileEquipo en su lugar.", true)]
    public class UsuarioTelegramEquipo
    {
        public int Id { get; set; }
        public int UsuarioTelegramId { get; set; }
        public int EquipoId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Propiedades de navegación
        public UsuarioTelegram? UsuarioTelegram { get; set; }
        public Equipo? Equipo { get; set; }
    }

    public enum RolEquipo
    {
        Admin,
        Miembro,
        Viewer
    }
}