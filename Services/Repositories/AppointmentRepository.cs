using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using RegistroCx.Models;
using RegistroCx.models;

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

    // Método principal con equipo_id y chat_id para tracking
    public async Task<long> SaveAsync(Appointment appointment, int equipoId, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO appointments 
                (equipo_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at)
            VALUES 
                (@EquipoId, @UserProfileId, @GoogleEmail, @FechaHora, @Lugar, @Cirujano, @Cirugia, @Cantidad, @Anestesiologo, @CalendarEventId, @CalendarSyncedAt, @ReminderSentAt, now())
            RETURNING id;";

        var parameters = new
        {
            EquipoId = equipoId,
            UserProfileId = appointment.UserProfileId,
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
        
        appointment.Id = id;
        appointment.EquipoId = equipoId;
        return id;
    }

    // Método de compatibilidad temporal (requiere resolver chatId a equipoId)
    public Task<long> SaveAsync(Appointment appointment, long chatId, CancellationToken ct)
    {
        // NOTA: Este método requiere que se inyecte EquipoService para resolver chatId -> equipoId
        // Por ahora, lanzar excepción indicando que debe usarse el método con equipoId
        throw new NotSupportedException(
            "Este método requiere migración. Use SaveAsync(appointment, equipoId, ct) en su lugar. " +
            "Para obtener equipoId desde chatId, use EquipoService.ObtenerPrimerEquipoIdPorChatIdAsync()");
    }

    public async Task<Appointment?> GetByIdAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, equipo_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
            FROM appointments 
            WHERE id = @id;";

        await using var conn = await OpenAsync(ct);
        
        var result = await conn.QueryFirstOrDefaultAsync(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));

        if (result == null) return null;

        return new Appointment
        {
            Id = result.id,
            EquipoId = result.equipo_id,
            UserProfileId = result.user_profile_id,
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

    // Este método se mueve a sección de compatibilidad al final del archivo

    public async Task<List<Appointment>> GetAppointmentsNeedingRemindersAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, equipo_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, 
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
            EquipoId = result.equipo_id,
            UserProfileId = result.user_profile_id,
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

    // Métodos movidos a sección de compatibilidad al final del archivo
    // GetAppointmentsByDateRangeAsync(string googleEmail...)
    // GetAppointmentsForWeekAsync(string googleEmail...)  
    // GetAppointmentsForMonthAsync(string googleEmail...)
    // GetAppointmentsForYearAsync(string googleEmail...)
    
    // Métodos antiguos eliminados - ver sección de compatibilidad al final

    public async Task UpdateAsync(long appointmentId, ModificationRequest changes, CancellationToken ct = default)
    {
        using var conn = await OpenAsync(ct);
        
        var updates = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("id", appointmentId);

        if (changes.NewDate.HasValue || changes.NewTime.HasValue)
        {
            // Obtener appointment actual para combinar fecha y hora
            var current = await GetByIdAsync(appointmentId, ct);
            if (current?.FechaHora.HasValue == true)
            {
                var newDate = changes.NewDate ?? current.FechaHora.Value.Date;
                var newTime = changes.NewTime ?? TimeOnly.FromDateTime(current.FechaHora.Value);
                var newDateTime = newDate.Add(newTime.ToTimeSpan());
                
                updates.Add("fecha_hora = @fechaHora");
                parameters.Add("fechaHora", newDateTime);
            }
        }

        if (!string.IsNullOrEmpty(changes.NewLocation))
        {
            updates.Add("lugar = @lugar");
            parameters.Add("lugar", changes.NewLocation);
        }

        if (!string.IsNullOrEmpty(changes.NewSurgeon))
        {
            updates.Add("cirujano = @cirujano");
            parameters.Add("cirujano", changes.NewSurgeon);
        }

        if (!string.IsNullOrEmpty(changes.NewSurgeryType))
        {
            updates.Add("cirugia = @cirugia");
            parameters.Add("cirugia", changes.NewSurgeryType);
        }

        if (changes.NewQuantity.HasValue)
        {
            updates.Add("cantidad = @cantidad");
            parameters.Add("cantidad", changes.NewQuantity.Value);
        }

        if (!string.IsNullOrEmpty(changes.NewAnesthesiologist))
        {
            updates.Add("anestesiologo = @anestesiologo");
            parameters.Add("anestesiologo", changes.NewAnesthesiologist);
        }

        if (updates.Any())
        {
            updates.Add("updated_at = now()");
            var sql = $"UPDATE appointments SET {string.Join(", ", updates)} WHERE id = @id";
            await conn.ExecuteAsync(sql, parameters);
        }
    }

    public async Task UpdateDirectAsync(long appointmentId, Appointment modifiedAppointment, CancellationToken ct = default)
    {
        using var conn = await OpenAsync(ct);
        
        const string sql = @"
            UPDATE appointments 
            SET fecha_hora = @fechaHora,
                lugar = @lugar,
                cirujano = @cirujano,
                cirugia = @cirugia,
                cantidad = @cantidad,
                anestesiologo = @anestesiologo,
                notas = @notas,
                updated_at = now()
            WHERE id = @id";

        var parameters = new DynamicParameters();
        parameters.Add("id", appointmentId);
        parameters.Add("fechaHora", modifiedAppointment.FechaHora);
        parameters.Add("lugar", modifiedAppointment.Lugar);
        parameters.Add("cirujano", modifiedAppointment.Cirujano);
        parameters.Add("cirugia", modifiedAppointment.Cirugia);
        parameters.Add("cantidad", modifiedAppointment.Cantidad);
        parameters.Add("anestesiologo", modifiedAppointment.Anestesiologo);
        parameters.Add("notas", modifiedAppointment.Notas);

        await conn.ExecuteAsync(sql, parameters);
    }

    #region Métodos con equipo_id

    public async Task<List<Appointment>> GetPendingCalendarSyncAsync(int equipoId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, equipo_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
            FROM appointments 
            WHERE equipo_id = @equipoId AND calendar_event_id IS NULL
            ORDER BY fecha_hora";

        await using var conn = await OpenAsync(ct);
        var results = await conn.QueryAsync(sql, new { equipoId });
        
        return results.Select(r => new Appointment
        {
            Id = r.id,
            EquipoId = r.equipo_id,
            UserProfileId = r.user_profile_id,
            GoogleEmail = r.google_email,
            FechaHora = r.fecha_hora,
            Lugar = r.lugar,
            Cirujano = r.cirujano,
            Cirugia = r.cirugia,
            Cantidad = r.cantidad,
            Anestesiologo = r.anestesiologo,
            CalendarEventId = r.calendar_event_id,
            CalendarSyncedAt = r.calendar_synced_at,
            ReminderSentAt = r.reminder_sent_at
        }).ToList();
    }

    public async Task<List<Appointment>> GetByEquipoAndDateRangeAsync(int equipoId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, equipo_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
            FROM appointments 
            WHERE equipo_id = @equipoId 
              AND fecha_hora >= @startDate 
              AND fecha_hora <= @endDate
            ORDER BY fecha_hora";

        await using var conn = await OpenAsync(ct);
        var results = await conn.QueryAsync(sql, new { equipoId, startDate, endDate });
        
        return results.Select(r => new Appointment
        {
            Id = r.id,
            EquipoId = r.equipo_id,
            UserProfileId = r.user_profile_id,
            GoogleEmail = r.google_email,
            FechaHora = r.fecha_hora,
            Lugar = r.lugar,
            Cirujano = r.cirujano,
            Cirugia = r.cirugia,
            Cantidad = r.cantidad,
            Anestesiologo = r.anestesiologo,
            CalendarEventId = r.calendar_event_id,
            CalendarSyncedAt = r.calendar_synced_at,
            ReminderSentAt = r.reminder_sent_at
        }).ToList();
    }

    public async Task<List<Appointment>> GetAppointmentsByDateRangeAsync(int equipoId, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        return await GetByEquipoAndDateRangeAsync(equipoId, startDate, endDate, ct);
    }

    public async Task<List<Appointment>> GetAppointmentsForWeekAsync(int equipoId, DateTime weekStartDate, CancellationToken ct)
    {
        var endDate = weekStartDate.AddDays(7);
        return await GetByEquipoAndDateRangeAsync(equipoId, weekStartDate, endDate, ct);
    }

    public async Task<List<Appointment>> GetAppointmentsForMonthAsync(int equipoId, int month, int year, CancellationToken ct)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);
        return await GetByEquipoAndDateRangeAsync(equipoId, startDate, endDate, ct);
    }

    public async Task<List<Appointment>> GetAppointmentsForYearAsync(int equipoId, int year, CancellationToken ct)
    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = new DateTime(year, 12, 31);
        return await GetByEquipoAndDateRangeAsync(equipoId, startDate, endDate, ct);
    }

    #endregion

    #region Métodos de compatibilidad temporal (lanzar excepciones indicando migración)

    public Task<List<Appointment>> GetPendingCalendarSyncAsync(long chatId, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetPendingCalendarSyncAsync(equipoId, ct) en su lugar. " +
            "Para obtener equipoId desde chatId, use EquipoService.ObtenerPrimerEquipoIdPorChatIdAsync()");
    }

    public Task<List<Appointment>> GetAppointmentsByDateRangeAsync(string googleEmail, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetAppointmentsByDateRangeAsync(equipoId, startDate, endDate, ct) en su lugar.");
    }

    public Task<List<Appointment>> GetAppointmentsForWeekAsync(string googleEmail, DateTime weekStartDate, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetAppointmentsForWeekAsync(equipoId, weekStartDate, ct) en su lugar.");
    }

    public Task<List<Appointment>> GetAppointmentsForMonthAsync(string googleEmail, int month, int year, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetAppointmentsForMonthAsync(equipoId, month, year, ct) en su lugar.");
    }

    public Task<List<Appointment>> GetAppointmentsForYearAsync(string googleEmail, int year, CancellationToken ct)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetAppointmentsForYearAsync(equipoId, year, ct) en su lugar.");
    }

    public Task<List<Appointment>> GetByUserAndDateRangeAsync(long chatId, DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "Este método requiere migración. Use GetByEquipoAndDateRangeAsync(equipoId, startDate, endDate, ct) en su lugar. " +
            "Para obtener equipoId desde chatId, use EquipoService.ObtenerPrimerEquipoIdPorChatIdAsync()");
    }

    #endregion

    #region Métodos de migración

    public async Task MigrateAppointmentsToEquipoAsync(long chatId, int equipoId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE appointments 
            SET equipo_id = @equipoId 
            WHERE user_profile_id = (SELECT id FROM user_profiles WHERE chat_id = @chatId) AND equipo_id IS NULL";

        await using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(sql, new { chatId, equipoId });
    }

    public async Task<List<Appointment>> GetAppointmentsWithoutEquipoAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, equipo_id, chat_id, user_profile_id, google_email, fecha_hora, lugar, cirujano, cirugia, cantidad, anestesiologo, calendar_event_id, calendar_synced_at, reminder_sent_at, created_at
            FROM appointments 
            WHERE equipo_id IS NULL
            ORDER BY fecha_hora";

        await using var conn = await OpenAsync(ct);
        var results = await conn.QueryAsync(sql);
        
        return results.Select(r => new Appointment
        {
            Id = r.id,
            EquipoId = r.equipo_id,
            UserProfileId = r.user_profile_id,
            GoogleEmail = r.google_email,
            FechaHora = r.fecha_hora,
            Lugar = r.lugar,
            Cirujano = r.cirujano,
            Cirugia = r.cirugia,
            Cantidad = r.cantidad,
            Anestesiologo = r.anestesiologo,
            CalendarEventId = r.calendar_event_id,
            CalendarSyncedAt = r.calendar_synced_at,
            ReminderSentAt = r.reminder_sent_at
        }).ToList();
    }

    #endregion
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