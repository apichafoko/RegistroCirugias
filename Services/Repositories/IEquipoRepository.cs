using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories
{
    public interface IEquipoRepository
    {
        // Operaciones básicas de Equipo
        Task<Equipo?> GetByIdAsync(int equipoId, CancellationToken ct = default);
        Task<Equipo?> GetByEmailAsync(string email, CancellationToken ct = default);
        Task<Equipo?> GetByNombreAsync(string nombre, CancellationToken ct = default);
        Task<List<Equipo>> GetAllAsync(CancellationToken ct = default);
        Task<List<Equipo>> GetActivosAsync(CancellationToken ct = default);
        Task<List<Equipo>> GetByEstadoSuscripcionAsync(EstadoSuscripcion estado, CancellationToken ct = default);
        Task<int> CreateAsync(Equipo equipo, CancellationToken ct = default);
        Task UpdateAsync(Equipo equipo, CancellationToken ct = default);
        Task DeleteAsync(int equipoId, CancellationToken ct = default);

        // Relaciones UserProfile - Equipo
        Task<List<UserProfile>> GetUserProfilesByEquipoAsync(int equipoId, CancellationToken ct = default);
        Task<List<Equipo>> GetEquiposByUserProfileAsync(int userProfileId, CancellationToken ct = default);
        Task<UserProfileEquipo?> GetUserProfileEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default);
        Task AddUserProfileToEquipoAsync(int userProfileId, int equipoId, RolEquipo rol = RolEquipo.Miembro, CancellationToken ct = default);
        Task RemoveUserProfileFromEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default);
        Task UpdateRolUserProfileEquipoAsync(int userProfileId, int equipoId, RolEquipo nuevoRol, CancellationToken ct = default);

        // Relaciones UsuarioTelegram - Equipo (OBSOLETAS - migrado a UserProfile)
        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        Task<List<UsuarioTelegram>> GetUsuariosTelegramByEquipoAsync(int equipoId, CancellationToken ct = default);
        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        Task<List<Equipo>> GetEquiposByUsuarioTelegramAsync(int usuarioTelegramId, CancellationToken ct = default);
        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        Task<UsuarioTelegramEquipo?> GetUsuarioTelegramEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default);
        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        Task AddUsuarioTelegramToEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default);
        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        Task RemoveUsuarioTelegramFromEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default);

        // Métodos de utilidad para migración y consultas por ChatId
        Task<Equipo?> GetEquipoByUserChatIdAsync(long chatId, CancellationToken ct = default);
        Task<List<int>> GetEquipoIdsByUserChatIdAsync(long chatId, CancellationToken ct = default);

        // Métodos para gestión de suscripciones
        Task<List<Equipo>> GetEquiposProximosAVencerAsync(int diasAntes = 7, CancellationToken ct = default);
        Task<List<Equipo>> GetEquiposVencidosAsync(CancellationToken ct = default);
        Task ActivarSuscripcionAsync(int equipoId, DateTime fechaPago, DateTime fechaVencimiento, CancellationToken ct = default);
        Task CancelarSuscripcionAsync(int equipoId, CancellationToken ct = default);
        Task RenovarSuscripcionAsync(int equipoId, DateTime nuevaFechaVencimiento, CancellationToken ct = default);

        // Métodos de validación
        Task<bool> ExisteEquipoConEmailAsync(string email, int? excludeId = null, CancellationToken ct = default);
        Task<bool> ExisteEquipoConNombreAsync(string nombre, int? excludeId = null, CancellationToken ct = default);
        Task<bool> UserPerteneceAEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default);
        Task<bool> TienePermisosAdminAsync(int userProfileId, int equipoId, CancellationToken ct = default);
    }
}