using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using RegistroCx.Domain;

namespace RegistroCx.Services.Repositories;

/// <summary>
/// Repositorio para manejar datos específicos de usuarios de Telegram
/// </summary>
public class UsuarioTelegramRepository : IUsuarioTelegramRepository
{
    private readonly string _connString;

    public UsuarioTelegramRepository(string connString)
    {
        _connString = connString;
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
            csb.TrustServerCertificate = true;

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

    public async Task<UsuarioTelegram?> GetByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                id AS Id,
                telegram_id AS TelegramId,
                nombre AS Nombre,
                username AS Username,
                telefono AS Telefono,
                email AS Email,
                calendar_autorizado AS CalendarAutorizado,
                chat_id AS ChatId
            FROM usuarios_telegram
            WHERE chat_id = @chatId";

        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<UsuarioTelegram>(
            new CommandDefinition(sql, new { chatId }, cancellationToken: ct));
    }

    public async Task<UsuarioTelegram> GetOrCreateAsync(long chatId, long telegramId, CancellationToken ct = default)
    {
        var usuario = await GetByChatIdAsync(chatId, ct);
        if (usuario == null)
        {
            const string insert = @"
                INSERT INTO usuarios_telegram (telegram_id, chat_id)
                VALUES (@telegramId, @chatId)
                RETURNING id, telegram_id, nombre, username, telefono, email, calendar_autorizado, chat_id";

            await using var conn = await OpenAsync(ct);
            usuario = await conn.QueryFirstAsync<UsuarioTelegram>(
                new CommandDefinition(insert, new { telegramId, chatId }, cancellationToken: ct));
        }

        return usuario;
    }

    public async Task SaveAsync(UsuarioTelegram usuario, CancellationToken ct = default)
    {
        const string upsert = @"
            INSERT INTO usuarios_telegram
                (telegram_id, nombre, username, telefono, email, calendar_autorizado, chat_id)
            VALUES
                (@TelegramId, @Nombre, @Username, @Telefono, @Email, @CalendarAutorizado, @ChatId)
            ON CONFLICT (telegram_id) DO UPDATE SET
                nombre = EXCLUDED.nombre,
                username = EXCLUDED.username,
                telefono = EXCLUDED.telefono,
                email = EXCLUDED.email,
                calendar_autorizado = EXCLUDED.calendar_autorizado,
                chat_id = EXCLUDED.chat_id";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(upsert, usuario, cancellationToken: ct));
    }

    public async Task UpdateTelegramDataAsync(
        long chatId,
        long telegramId,
        string? nombre,
        string? username,
        string? telefono = null,
        string? email = null,
        CancellationToken ct = default)
    {
        const string upsert = @"
            INSERT INTO usuarios_telegram
                (telegram_id, nombre, username, telefono, email, chat_id)
            VALUES
                (@telegramId, @nombre, @username, @telefono, @email, @chatId)
            ON CONFLICT (telegram_id) DO UPDATE SET
                nombre = COALESCE(EXCLUDED.nombre, usuarios_telegram.nombre),
                username = COALESCE(EXCLUDED.username, usuarios_telegram.username),
                telefono = COALESCE(EXCLUDED.telefono, usuarios_telegram.telefono),
                email = COALESCE(EXCLUDED.email, usuarios_telegram.email),
                chat_id = EXCLUDED.chat_id";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(upsert, new
        {
            telegramId,
            nombre,
            username,
            telefono,
            email,
            chatId
        }, cancellationToken: ct));
    }
}