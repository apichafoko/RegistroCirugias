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
                (chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at)
            VALUES 
                (@ChatId, @GoogleEmail, @FechaHora, @Lugar, @Cirujano, @Cirugia, @Cantidad, @Anestesiologo, @CalendarEventId, @CalendarSyncedAt, @ReminderSentAt, now())
            RETURNING id;";

        var parameters = new
        {
            ChatId = chatId,
            GoogleEmail = appointment.GoogleEmail,
            FechaHora = appointment.FechaHora,
            Lugar = appointment.Lugar,
            Cirujano = appointment.Cirujano,
            Cirugia = appointment.Cirugia,
            Cantidad = appointment.Cantidad,
            Anestesiologo = appointment.Anestesiologo,
            CalendarEventId = appointment.CalendarEventId,
            CalendarSyncedAt = appointment.CalendarSyncedAt,
            ReminderSentAt = appointment.ReminderSentAt
        };

        await using var conn = await OpenAsync(ct);
        var id = await conn.QuerySingleAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return id;
    }

    public async Task<Appointment?> GetByIdAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
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
            Id = result.id,
            ChatId = result.chat_id,
            GoogleEmail = result.google_email,
            FechaHora = result.fecha_hora,
            Lugar = result.lugar,
            Cirujano = result.cirujano,
            Cirugia = result.cirugia,
            Cantidad = result.cantidad,
            Anestesiologo = result.anestesiologo,
            CalendarEventId = result.calendar_event_id,
            CalendarSyncedAt = result.calendar_synced_at,
            ReminderSentAt = result.reminder_sent_at
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
            SELECT id, chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, 
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
            GoogleEmail = result.google_email,
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

    public async Task<List<Appointment>> GetAppointmentsNeedingRemindersAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, chat_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, 
                   calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
            FROM appointments 
            WHERE reminder_sent_at IS NULL 
              AND fecha_hora > now()
              AND fecha_hora <= now() + interval '24 hours'
            ORDER BY fecha_hora ASC;";

        await using var conn = await OpenAsync(ct);
        var results = await conn.QueryAsync(
            new CommandDefinition(sql, cancellationToken: ct));

        return results.Select(result => new Appointment
        {
            Id = result.id,
            ChatId = result.chat_id,
            GoogleEmail = result.google_email,
            FechaHora = result.fecha_hora,
            Lugar = result.lugar,
            Cirujano = result.cirujano,
            Cirugia = result.cirugia,
            Cantidad = result.cantidad,
            Anestesiologo = result.anestesiologo,
            CalendarEventId = result.calendar_event_id,
            CalendarSyncedAt = result.calendar_synced_at,
            ReminderSentAt = result.reminder_sent_at
        }).ToList();
    }

    public async Task MarkReminderSentAsync(long appointmentId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE appointments 
            SET reminder_sent_at = now() 
            WHERE id = @appointmentId;";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { appointmentId }, cancellationToken: ct));
    }

    public async Task<List<Appointment>> GetAppointmentsByDateRangeAsync(string googleEmail, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        Console.WriteLine($"[APPOINTMENT-REPO] Querying appointments for email: '{googleEmail}', from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
        
        const string sql = @"
            SELECT id AS Id,
                   chat_id AS ChatId,
                   google_email AS GoogleEmail,
                   fecha_hora AS FechaHora,
                   lugar AS Lugar,
                   cirujano AS Cirujano,
                   cirugia AS Cirugia,
                   cantidad AS Cantidad,
                   anestesiologo AS Anestesiologo,
                   calendar_event_id AS CalendarEventId,
                   calendar_synced_at AS CalendarSyncedAt,
                   reminder_sent_at AS ReminderSentAt
            FROM appointments 
            WHERE google_email = @googleEmail 
              AND fecha_hora >= @startDate 
              AND fecha_hora <= @endDate
            ORDER BY fecha_hora";

        await using var conn = await OpenAsync(ct);
        var appointments = await conn.QueryAsync<Appointment>(
            new CommandDefinition(sql, new { googleEmail, startDate, endDate }, cancellationToken: ct));
            
        Console.WriteLine($"[APPOINTMENT-REPO] Found {appointments.Count()} appointments for email '{googleEmail}' in date range");
        
        return appointments.ToList();
    }

    public async Task<List<Appointment>> GetAppointmentsForWeekAsync(string googleEmail, DateTime weekStartDate, CancellationToken ct)
    {
        var weekEndDate = weekStartDate.AddDays(6).Date.AddDays(1).AddTicks(-1); // Final del día
        return await GetAppointmentsByDateRangeAsync(googleEmail, weekStartDate, weekEndDate, ct);
    }

    public async Task<List<Appointment>> GetAppointmentsForMonthAsync(string googleEmail, int month, int year, CancellationToken ct)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddTicks(-1);
        return await GetAppointmentsByDateRangeAsync(googleEmail, startDate, endDate, ct);
    }

    public async Task<List<Appointment>> GetAppointmentsForYearAsync(string googleEmail, int year, CancellationToken ct)
    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = new DateTime(year, 12, 31, 23, 59, 59);
        return await GetAppointmentsByDateRangeAsync(googleEmail, startDate, endDate, ct);
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
    anestesiologo TEXT NULL,
    calendar_event_id TEXT NULL,
    calendar_synced_at TIMESTAMP NULL,
    reminder_sent_at TIMESTAMP NULL,
    created_at TIMESTAMP DEFAULT now(),
    updated_at TIMESTAMP DEFAULT now(),
    
    -- Foreign key opcional si quieres relacionar con user_profiles
    FOREIGN KEY (chat_id) REFERENCES user_profiles(chat_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_appointments_chat_id ON appointments(chat_id);
CREATE INDEX IF NOT EXISTS idx_appointments_fecha_hora ON appointments(fecha_hora);
*/