using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RegistroCx.Domain;
using RegistroCx.Services.Repositories;

namespace RegistroCx.Services
{
    public class EquipoService
    {
        private readonly IEquipoRepository _equipoRepo;
        private readonly IUserProfileRepository _userProfileRepo;
        private readonly ILogger<EquipoService> _logger;

        public EquipoService(
            IEquipoRepository equipoRepo,
            IUserProfileRepository userProfileRepo,
            ILogger<EquipoService> logger)
        {
            _equipoRepo = equipoRepo;
            _userProfileRepo = userProfileRepo;
            _logger = logger;
        }

        #region Gestión de Equipos

        public async Task<Equipo> CrearEquipoAsync(string nombre, string email, CancellationToken ct = default)
        {
            _logger.LogInformation("[EQUIPO-SERVICE] Creando equipo: {nombre}, {email}", nombre, email);

            // Validaciones
            if (await _equipoRepo.ExisteEquipoConNombreAsync(nombre, null, ct))
            {
                throw new InvalidOperationException($"Ya existe un equipo con el nombre '{nombre}'");
            }

            if (await _equipoRepo.ExisteEquipoConEmailAsync(email, null, ct))
            {
                throw new InvalidOperationException($"Ya existe un equipo con el email '{email}'");
            }

            var equipo = new Equipo
            {
                Nombre = nombre,
                Email = email,
                EstadoSuscripcion = EstadoSuscripcion.Prueba,
                Estado = EstadoEquipo.Activo
            };

            var equipoId = await _equipoRepo.CreateAsync(equipo, ct);
            equipo.Id = equipoId;

            _logger.LogInformation("[EQUIPO-SERVICE] Equipo creado con ID: {equipoId}", equipoId);
            return equipo;
        }

        public async Task<Equipo?> ObtenerEquipoPorChatIdAsync(long chatId, CancellationToken ct = default)
        {
            return await _equipoRepo.GetEquipoByUserChatIdAsync(chatId, ct);
        }

        public async Task<List<int>> ObtenerEquipoIdsPorChatIdAsync(long chatId, CancellationToken ct = default)
        {
            return await _equipoRepo.GetEquipoIdsByUserChatIdAsync(chatId, ct);
        }

        public async Task<int> ObtenerPrimerEquipoIdPorChatIdAsync(long chatId, CancellationToken ct = default)
        {
            var equipoIds = await ObtenerEquipoIdsPorChatIdAsync(chatId, ct);
            if (equipoIds.Count == 0)
            {
                throw new InvalidOperationException($"El usuario con chatId {chatId} no pertenece a ningún equipo");
            }
            return equipoIds[0]; // Devolver el primer equipo
        }

        public async Task ActivarSuscripcionAsync(int equipoId, DateTime fechaVencimiento, CancellationToken ct = default)
        {
            _logger.LogInformation("[EQUIPO-SERVICE] Activando suscripción para equipo: {equipoId}", equipoId);
            
            var fechaPago = DateTime.UtcNow;
            await _equipoRepo.ActivarSuscripcionAsync(equipoId, fechaPago, fechaVencimiento, ct);
        }

        public async Task<List<Equipo>> ObtenerEquiposProximosAVencerAsync(int diasAntes = 7, CancellationToken ct = default)
        {
            return await _equipoRepo.GetEquiposProximosAVencerAsync(diasAntes, ct);
        }

        #endregion

        #region Gestión de Miembros

        public async Task AgregarUserProfileAEquipoAsync(int userProfileId, int equipoId, RolEquipo rol = RolEquipo.Miembro, CancellationToken ct = default)
        {
            _logger.LogInformation("[EQUIPO-SERVICE] Agregando user {userProfileId} al equipo {equipoId} con rol {rol}", 
                userProfileId, equipoId, rol);

            await _equipoRepo.AddUserProfileToEquipoAsync(userProfileId, equipoId, rol, ct);
        }

        [Obsolete("Los datos de Telegram ahora están en UserProfile. Use AgregarUserProfileAEquipoAsync en su lugar.")]
        public Task AgregarUsuarioTelegramAEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use AgregarUserProfileAEquipoAsync en su lugar.");
        }

        public async Task<bool> UsuarioPerteneceAEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            return await _equipoRepo.UserPerteneceAEquipoAsync(userProfileId, equipoId, ct);
        }

        public async Task<bool> UsuarioEsAdminDeEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            return await _equipoRepo.TienePermisosAdminAsync(userProfileId, equipoId, ct);
        }

        public async Task<List<UserProfile>> ObtenerMiembrosDelEquipoAsync(int equipoId, CancellationToken ct = default)
        {
            return await _equipoRepo.GetUserProfilesByEquipoAsync(equipoId, ct);
        }

        #endregion

        #region Métodos de Migración y Utilidad

        /// <summary>
        /// Migra un usuario existente (por chatId) al sistema de equipos
        /// Útil durante la transición al nuevo sistema
        /// </summary>
        public async Task MigrarUsuarioAEquipoAsync(long chatId, int equipoId, RolEquipo rol = RolEquipo.Miembro, CancellationToken ct = default)
        {
            _logger.LogInformation("[EQUIPO-SERVICE] Migrando usuario chatId {chatId} al equipo {equipoId}", chatId, equipoId);

            // Buscar user profile (que ahora incluye todos los datos de Telegram)
            var userProfile = await _userProfileRepo.GetAsync(chatId, ct);
            if (userProfile != null)
            {
                await AgregarUserProfileAEquipoAsync(userProfile.Id, equipoId, rol, ct);
                _logger.LogInformation("[EQUIPO-SERVICE] Usuario migrado exitosamente");
            }
            else
            {
                _logger.LogWarning("[EQUIPO-SERVICE] No se encontró user profile para chatId {chatId}", chatId);
            }
        }

        /// <summary>
        /// Crea un equipo y migra automáticamente todos los datos de un usuario
        /// </summary>
        public async Task<Equipo> CrearEquipoYMigrarUsuarioAsync(long chatId, string nombreEquipo, string emailEquipo, CancellationToken ct = default)
        {
            _logger.LogInformation("[EQUIPO-SERVICE] Creando equipo y migrando usuario chatId {chatId}", chatId);

            // Crear equipo
            var equipo = await CrearEquipoAsync(nombreEquipo, emailEquipo, ct);

            // Migrar usuario como admin
            await MigrarUsuarioAEquipoAsync(chatId, equipo.Id, RolEquipo.Admin, ct);

            return equipo;
        }

        /// <summary>
        /// Obtiene información completa del equipo incluyendo estadísticas
        /// </summary>
        public async Task<EquipoInfo> ObtenerInfoCompletaEquipoAsync(int equipoId, CancellationToken ct = default)
        {
            var equipo = await _equipoRepo.GetByIdAsync(equipoId, ct);
            if (equipo == null)
            {
                throw new ArgumentException($"No se encontró el equipo con ID {equipoId}");
            }

            var miembros = await _equipoRepo.GetUserProfilesByEquipoAsync(equipoId, ct);

            return new EquipoInfo
            {
                Equipo = equipo,
                TotalMiembros = miembros.Count,
                Miembros = miembros
            };
        }

        #endregion

        #region Validaciones y Helpers

        public async Task ValidarAccesoAEquipoAsync(long chatId, int equipoId, CancellationToken ct = default)
        {
            var userProfile = await _userProfileRepo.GetAsync(chatId, ct);
            if (userProfile == null)
            {
                throw new UnauthorizedAccessException("Usuario no encontrado");
            }

            if (!await UsuarioPerteneceAEquipoAsync(userProfile.Id, equipoId, ct))
            {
                throw new UnauthorizedAccessException("El usuario no pertenece a este equipo");
            }
        }

        public async Task ValidarAccesoAdminAEquipoAsync(long chatId, int equipoId, CancellationToken ct = default)
        {
            var userProfile = await _userProfileRepo.GetAsync(chatId, ct);
            if (userProfile == null)
            {
                throw new UnauthorizedAccessException("Usuario no encontrado");
            }

            if (!await UsuarioEsAdminDeEquipoAsync(userProfile.Id, equipoId, ct))
            {
                throw new UnauthorizedAccessException("El usuario no tiene permisos de administrador en este equipo");
            }
        }

        #endregion
    }

    public class EquipoInfo
    {
        public Equipo Equipo { get; set; } = null!;
        public int TotalMiembros { get; set; }
        public List<UserProfile> Miembros { get; set; } = new();
    }
}