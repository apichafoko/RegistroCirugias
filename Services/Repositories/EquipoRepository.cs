using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories
{
    public class EquipoRepository : IEquipoRepository
    {
        private readonly string _connString;

        public EquipoRepository(string connectionString)
        {
            _connString = connectionString;
        }

        private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var csb = new NpgsqlConnectionStringBuilder();

            if (_connString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                // Parsear URI
                var uri = new Uri(_connString);
                var userInfo = uri.UserInfo.Split(':', 2);
                csb.Host = uri.Host;
                if (uri.Port > 0)
                    csb.Port = uri.Port;
                csb.Username = userInfo[0];
                csb.Password = userInfo.Length > 1 ? userInfo[1] : "";
                csb.Database = uri.AbsolutePath.TrimStart('/');

                // leer parámetros de query
                var qp = System.Web.HttpUtility.ParseQueryString(uri.Query);
                if (qp["sslmode"] != null)
                    csb.SslMode = Enum.Parse<SslMode>(qp["sslmode"]!, ignoreCase: true);
            }
            else
            {
                // asume ya viene en formato clave=valor
                csb.ConnectionString = _connString;
            }

            var conn = new NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        #region Operaciones básicas de Equipo

        public async Task<Equipo?> GetByIdAsync(int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE id = @equipoId";
            
            return await connection.QuerySingleOrDefaultAsync<Equipo>(sql, new { equipoId });
        }

        public async Task<Equipo?> GetByEmailAsync(string email, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE email = @email";
            
            return await connection.QuerySingleOrDefaultAsync<Equipo>(sql, new { email });
        }

        public async Task<Equipo?> GetByNombreAsync(string nombre, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE nombre = @nombre";
            
            return await connection.QuerySingleOrDefaultAsync<Equipo>(sql, new { nombre });
        }

        public async Task<List<Equipo>> GetAllAsync(CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                ORDER BY nombre";
            
            var result = await connection.QueryAsync<Equipo>(sql);
            return result.ToList();
        }

        public async Task<List<Equipo>> GetActivosAsync(CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE estado = 'activo'
                ORDER BY nombre";
            
            var result = await connection.QueryAsync<Equipo>(sql);
            return result.ToList();
        }

        public async Task<List<Equipo>> GetByEstadoSuscripcionAsync(EstadoSuscripcion estado, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE estado_suscripcion = @estado::varchar
                ORDER BY nombre";
            
            var result = await connection.QueryAsync<Equipo>(sql, new { estado = estado.ToString().ToLower() });
            return result.ToList();
        }

        public async Task<int> CreateAsync(Equipo equipo, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                INSERT INTO equipos (nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, estado)
                VALUES (@Nombre, @Email, @EstadoSuscripcion::varchar, @FechaPago, @FechaVencimiento, @Estado::varchar)
                RETURNING id";
            
            var id = await connection.QuerySingleAsync<int>(sql, new
            {
                equipo.Nombre,
                equipo.Email,
                EstadoSuscripcion = equipo.EstadoSuscripcion.ToString().ToLower(),
                equipo.FechaPago,
                equipo.FechaVencimiento,
                Estado = equipo.Estado.ToString().ToLower()
            });
            
            equipo.Id = id;
            return id;
        }

        public async Task UpdateAsync(Equipo equipo, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                UPDATE equipos 
                SET nombre = @Nombre, 
                    email = @Email, 
                    estado_suscripcion = @EstadoSuscripcion::varchar, 
                    fecha_pago = @FechaPago, 
                    fecha_vencimiento = @FechaVencimiento, 
                    estado = @Estado::varchar,
                    updated_at = NOW()
                WHERE id = @Id";
            
            await connection.ExecuteAsync(sql, new
            {
                equipo.Id,
                equipo.Nombre,
                equipo.Email,
                EstadoSuscripcion = equipo.EstadoSuscripcion.ToString().ToLower(),
                equipo.FechaPago,
                equipo.FechaVencimiento,
                Estado = equipo.Estado.ToString().ToLower()
            });
        }

        public async Task DeleteAsync(int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = "DELETE FROM equipos WHERE id = @equipoId";
            await connection.ExecuteAsync(sql, new { equipoId });
        }

        #endregion

        #region Relaciones UserProfile - Equipo

        public async Task<List<UserProfile>> GetUserProfilesByEquipoAsync(int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT up.id, up.chat_id, up.phone, up.google_email, up.state, up.created_at, up.updated_at
                FROM user_profiles up
                INNER JOIN user_profile_equipos upe ON up.id = upe.user_profile_id
                WHERE upe.equipo_id = @equipoId
                ORDER BY up.google_email";
            
            var result = await connection.QueryAsync<UserProfile>(sql, new { equipoId });
            return result.ToList();
        }

        public async Task<List<Equipo>> GetEquiposByUserProfileAsync(int userProfileId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT e.id, e.nombre, e.email, e.estado_suscripcion, e.fecha_pago, e.fecha_vencimiento, 
                       e.estado, e.created_at, e.updated_at
                FROM equipos e
                INNER JOIN user_profile_equipos upe ON e.id = upe.equipo_id
                WHERE upe.user_profile_id = @userProfileId
                ORDER BY e.nombre";
            
            var result = await connection.QueryAsync<Equipo>(sql, new { userProfileId });
            return result.ToList();
        }

        public async Task<UserProfileEquipo?> GetUserProfileEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, user_profile_id, equipo_id, rol, created_at
                FROM user_profile_equipos 
                WHERE user_profile_id = @userProfileId AND equipo_id = @equipoId";
            
            return await connection.QuerySingleOrDefaultAsync<UserProfileEquipo>(sql, new { userProfileId, equipoId });
        }

        public async Task AddUserProfileToEquipoAsync(int userProfileId, int equipoId, RolEquipo rol = RolEquipo.Miembro, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                INSERT INTO user_profile_equipos (user_profile_id, equipo_id, rol)
                VALUES (@userProfileId, @equipoId, @rol::varchar)
                ON CONFLICT (user_profile_id, equipo_id) DO NOTHING";
            
            await connection.ExecuteAsync(sql, new { userProfileId, equipoId, rol = rol.ToString().ToLower() });
        }

        public async Task RemoveUserProfileFromEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = "DELETE FROM user_profile_equipos WHERE user_profile_id = @userProfileId AND equipo_id = @equipoId";
            await connection.ExecuteAsync(sql, new { userProfileId, equipoId });
        }

        public async Task UpdateRolUserProfileEquipoAsync(int userProfileId, int equipoId, RolEquipo nuevoRol, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                UPDATE user_profile_equipos 
                SET rol = @nuevoRol::varchar
                WHERE user_profile_id = @userProfileId AND equipo_id = @equipoId";
            
            await connection.ExecuteAsync(sql, new { userProfileId, equipoId, nuevoRol = nuevoRol.ToString().ToLower() });
        }

        #endregion

        #region Relaciones UsuarioTelegram - Equipo (OBSOLETAS)

        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use GetUserProfilesByEquipoAsync en su lugar.")]
        public Task<List<UsuarioTelegram>> GetUsuariosTelegramByEquipoAsync(int equipoId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use GetUserProfilesByEquipoAsync en su lugar.");
        }

        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        public Task<List<Equipo>> GetEquiposByUsuarioTelegramAsync(int usuarioTelegramId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use GetEquiposByUserProfileAsync en su lugar.");
        }

        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use métodos de UserProfile en su lugar.")]
        public Task<UsuarioTelegramEquipo?> GetUsuarioTelegramEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use GetUserProfileEquipoAsync en su lugar.");
        }

        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use AddUserProfileToEquipoAsync en su lugar.")]
        public Task AddUsuarioTelegramToEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use AddUserProfileToEquipoAsync en su lugar.");
        }

        [Obsolete("Los datos de Telegram han sido migrados a UserProfile. Use RemoveUserProfileFromEquipoAsync en su lugar.")]
        public Task RemoveUsuarioTelegramFromEquipoAsync(int usuarioTelegramId, int equipoId, CancellationToken ct = default)
        {
            throw new NotSupportedException("Los datos de Telegram han sido migrados a UserProfile. Use RemoveUserProfileFromEquipoAsync en su lugar.");
        }

        #endregion

        #region Métodos de utilidad para migración y consultas por ChatId

        public async Task<Equipo?> GetEquipoByUserChatIdAsync(long chatId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT DISTINCT e.id, e.nombre, e.email, e.estado_suscripcion, e.fecha_pago, e.fecha_vencimiento, 
                       e.estado, e.created_at, e.updated_at
                FROM equipos e
                INNER JOIN user_profile_equipos upe ON e.id = upe.equipo_id
                INNER JOIN user_profiles up ON upe.user_profile_id = up.id
                WHERE up.chat_id = @chatId
                ORDER BY e.id
                LIMIT 1";
            
            return await connection.QuerySingleOrDefaultAsync<Equipo>(sql, new { chatId });
        }

        public async Task<List<int>> GetEquipoIdsByUserChatIdAsync(long chatId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT DISTINCT e.id
                FROM equipos e
                INNER JOIN user_profile_equipos upe ON e.id = upe.equipo_id
                INNER JOIN user_profiles up ON upe.user_profile_id = up.id
                WHERE up.chat_id = @chatId
                ORDER BY e.id";
            
            var result = await connection.QueryAsync<int>(sql, new { chatId });
            return result.ToList();
        }

        #endregion

        #region Métodos para gestión de suscripciones

        public async Task<List<Equipo>> GetEquiposProximosAVencerAsync(int diasAntes = 7, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE estado_suscripcion = 'pago' 
                  AND estado = 'activo'
                  AND fecha_vencimiento IS NOT NULL
                  AND fecha_vencimiento <= @fechaLimite
                  AND fecha_vencimiento > NOW()
                ORDER BY fecha_vencimiento";
            
            var fechaLimite = DateTime.UtcNow.AddDays(diasAntes);
            var result = await connection.QueryAsync<Equipo>(sql, new { fechaLimite });
            return result.ToList();
        }

        public async Task<List<Equipo>> GetEquiposVencidosAsync(CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT id, nombre, email, estado_suscripcion, fecha_pago, fecha_vencimiento, 
                       estado, created_at, updated_at
                FROM equipos 
                WHERE estado_suscripcion = 'pago' 
                  AND fecha_vencimiento IS NOT NULL
                  AND fecha_vencimiento <= NOW()
                ORDER BY fecha_vencimiento";
            
            var result = await connection.QueryAsync<Equipo>(sql);
            return result.ToList();
        }

        public async Task ActivarSuscripcionAsync(int equipoId, DateTime fechaPago, DateTime fechaVencimiento, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                UPDATE equipos 
                SET estado_suscripcion = 'pago', 
                    fecha_pago = @fechaPago, 
                    fecha_vencimiento = @fechaVencimiento, 
                    estado = 'activo',
                    updated_at = NOW()
                WHERE id = @equipoId";
            
            await connection.ExecuteAsync(sql, new { equipoId, fechaPago, fechaVencimiento });
        }

        public async Task CancelarSuscripcionAsync(int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                UPDATE equipos 
                SET estado_suscripcion = 'cancelado',
                    updated_at = NOW()
                WHERE id = @equipoId";
            
            await connection.ExecuteAsync(sql, new { equipoId });
        }

        public async Task RenovarSuscripcionAsync(int equipoId, DateTime nuevaFechaVencimiento, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                UPDATE equipos 
                SET fecha_vencimiento = @nuevaFechaVencimiento, 
                    fecha_pago = NOW(), 
                    estado_suscripcion = 'pago', 
                    estado = 'activo',
                    updated_at = NOW()
                WHERE id = @equipoId";
            
            await connection.ExecuteAsync(sql, new { equipoId, nuevaFechaVencimiento });
        }

        #endregion

        #region Métodos de validación

        public async Task<bool> ExisteEquipoConEmailAsync(string email, int? excludeId = null, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = "SELECT COUNT(*) FROM equipos WHERE email = @email";
            if (excludeId.HasValue)
            {
                sql += " AND id != @excludeId";
            }
            
            var count = await connection.QuerySingleAsync<int>(sql, new { email, excludeId });
            return count > 0;
        }

        public async Task<bool> ExisteEquipoConNombreAsync(string nombre, int? excludeId = null, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = "SELECT COUNT(*) FROM equipos WHERE nombre = @nombre";
            if (excludeId.HasValue)
            {
                sql += " AND id != @excludeId";
            }
            
            var count = await connection.QuerySingleAsync<int>(sql, new { nombre, excludeId });
            return count > 0;
        }

        public async Task<bool> UserPerteneceAEquipoAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = "SELECT COUNT(*) FROM user_profile_equipos WHERE user_profile_id = @userProfileId AND equipo_id = @equipoId";
            var count = await connection.QuerySingleAsync<int>(sql, new { userProfileId, equipoId });
            return count > 0;
        }

        public async Task<bool> TienePermisosAdminAsync(int userProfileId, int equipoId, CancellationToken ct = default)
        {
            await using var connection = await OpenAsync(ct);
            
            var sql = @"
                SELECT COUNT(*) 
                FROM user_profile_equipos 
                WHERE user_profile_id = @userProfileId 
                  AND equipo_id = @equipoId 
                  AND rol = 'admin'";
            
            var count = await connection.QuerySingleAsync<int>(sql, new { userProfileId, equipoId });
            return count > 0;
        }

        #endregion
    }
}