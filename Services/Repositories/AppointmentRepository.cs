using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using RegistroCx.Models;

namespace RegistroCx.Services.Repositories;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly string _connString;
    
    public AppointmentRepository(string connString) => _connString = connString;

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

    public async Task<long> SaveAsync(Appointment appointment, long chatId, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO appointments 
                (chat_id, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, created_at)
            VALUES 
                (@ChatId, @FechaHora, @Lugar, @Cirujano, @Cirugia, @Cantidad, @Anestesiologo, @CalendarEventId, @CalendarSyncedAt, now())
            RETURNING id;";

        var parameters = new
        {
            ChatId = chatId,
            FechaHora = appointment.FechaHora,
            Lugar = appointment.Lugar,
            Cirujano = appointment.Cirujano,
            Cirugia = appointment.Cirugia,
            Cantidad = appointment.Cantidad,
            Anestesiologo = appointment.Anestesiologo,
            CalendarEventId = appointment.CalendarEventId,
            CalendarSyncedAt = appointment.CalendarSyncedAt
        };

        await using var conn = await OpenAsync(ct);
        var id = await conn.QuerySingleAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return id;
    }

    public async Task<Appointment?> GetByIdAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, chat_id, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, created_at
            FROM appointments 
            WHERE id = @id;";

        await using var conn = await OpenAsync(ct);
        
        // Note: We'll need to map the result to an Appointment object
        // This is a simplified version - you may need to adjust based on your Appointment model
        var result = await conn.QueryFirstOrDefaultAsync(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));

        if (result == null) return null;

        return new Appointment
        {
            ChatId = result.chat_id,
            FechaHora = result.fecha_hora,
            Lugar = result.lugar,
            Cirujano = result.cirujano,
            Cirugia = result.cirugia,
            Cantidad = result.cantidad,
            Anestesiologo = result.anestesiologo
        };
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = @"DELETE FROM appointments WHERE id = @id;";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task UpdateCalendarEventAsync(long appointmentId, string eventId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE appointments 
            SET calendar_event_id = @eventId, 
                calendar_synced_at = now() 
            WHERE id = @appointmentId;";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { appointmentId, eventId }, cancellationToken: ct));
    }

    public async Task<List<Appointment>> GetPendingCalendarSyncAsync(long chatId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, chat_id, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, 
                   calendar_event_id, calendar_synced_at, created_at
            FROM appointments 
            WHERE chat_id = @chatId 
              AND calendar_event_id IS NULL 
              AND fecha_hora > now()
            ORDER BY fecha_hora ASC;";

        await using var conn = await OpenAsync(ct);
        var results = await conn.QueryAsync(
            new CommandDefinition(sql, new { chatId }, cancellationToken: ct));

        return results.Select(result => new Appointment
        {
            Id = result.id,
            ChatId = result.chat_id,
            FechaHora = result.fecha_hora,
            Lugar = result.lugar,
            Cirujano = result.cirujano,
            Cirugia = result.cirugia,
            Cantidad = result.cantidad,
            Anestesiologo = result.anestesiologo,
            CalendarEventId = result.calendar_event_id,
            CalendarSyncedAt = result.calendar_synced_at
        }).ToList();
    }
}

/*
-- SQL para crear/actualizar la tabla appointments:

CREATE TABLE IF NOT EXISTS appointments (
    id BIGSERIAL PRIMARY KEY,
    chat_id BIGINT NOT NULL,
    fecha_hora TIMESTAMP NOT NULL,
    lugar TEXT NOT NULL,
    cirujano TEXT NOT NULL,
    cirugia TEXT NOT NULL,
    cantidad INTEGER NOT NULL,
    anestesiologo TEXT NOT NULL,
    calendar_event_id TEXT NULL,
    calendar_synced_at TIMESTAMP NULL,
    created_at TIMESTAMP DEFAULT now(),
    updated_at TIMESTAMP DEFAULT now(),
    
    -- Foreign key opcional si quieres relacionar con user_profiles
    FOREIGN KEY (chat_id) REFERENCES user_profiles(chat_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_appointments_chat_id ON appointments(chat_id);
CREATE INDEX IF NOT EXISTS idx_appointments_fecha_hora ON appointments(fecha_hora);
*/