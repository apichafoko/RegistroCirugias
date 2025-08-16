using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace RegistroCx.Services.Repositories;

public class AnesthesiologistRepository : IAnesthesiologistRepository
{
    private readonly string _connString;
    
    public AnesthesiologistRepository(string connString) => _connString = connString;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder();

        if (_connString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Parsear URI
            var uri = new Uri(_connString);
            var userInfo = uri.UserInfo.Split(':', 2);
            csb.Host = uri.Host;
            if (uri.Port > 0)              // sólo asigna si está explícito
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

    public async Task<string?> GetEmailByNameAsync(string fullName, CancellationToken ct)
    {
        // Buscar por coincidencia en nombre + apellido (case insensitive)
        const string sql = @"
            SELECT email 
            FROM anestesiologos 
            WHERE LOWER(CONCAT(nombre, ' ', apellido)) = LOWER(@fullName)
            OR LOWER(CONCAT(apellido, ' ', nombre)) = LOWER(@fullName)
            LIMIT 1;";

        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(sql, new { fullName }, cancellationToken: ct));
    }

    public async Task<string?> GetEmailByNicknameAsync(string nickname, CancellationToken ct)
    {
        // Buscar en el array de nicknames
        const string sql = @"
            SELECT email 
            FROM anestesiologos 
            WHERE LOWER(@nickname) = ANY(SELECT LOWER(unnest(nicknames)))
            LIMIT 1;";

        await using var conn = await OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(sql, new { nickname }, cancellationToken: ct));
    }

    public async Task SaveAsync(string nombre, string apellido, string email, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO anestesiologos (nombre, apellido, email) 
            VALUES (@nombre, @apellido, @email)
            ON CONFLICT (email) 
            DO UPDATE SET 
                nombre = EXCLUDED.nombre,
                apellido = EXCLUDED.apellido;";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { nombre, apellido, email }, cancellationToken: ct));
    }

    public async Task AddNicknameAsync(long anesthesiologistId, string nickname, CancellationToken ct)
    {
        const string sql = @"
            UPDATE anestesiologos 
            SET nicknames = array_append(nicknames, @nickname)
            WHERE id = @anesthesiologistId
            AND NOT (@nickname = ANY(nicknames));"; // Evitar duplicados

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { anesthesiologistId, nickname }, cancellationToken: ct));
    }
}