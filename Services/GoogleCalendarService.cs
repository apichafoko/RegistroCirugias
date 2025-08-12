using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using RegistroCx.Models;
using RegistroCx.Services.Repositories;
using RegistroCx.Helpers._0Auth;
using Google;

namespace RegistroCx.Services;

public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IUserProfileRepository _userRepo;
    private readonly IGoogleOAuthService _oauthService;

    public GoogleCalendarService(IUserProfileRepository userRepo, IGoogleOAuthService oauthService)
    {
        _userRepo = userRepo;
        _oauthService = oauthService;
    }

    public async Task<string> CreateAppointmentEventAsync(
        Appointment appointment, 
        long chatId, 
        CancellationToken ct)
    {
        return await ExecuteWithAuthRetryAsync(chatId, async calendarService =>
        {
            // Crear el evento
            var calendarEvent = new Event
            {
                Summary = BuildEventTitle(appointment),
                Description = BuildEventDescription(appointment),
                Location = appointment.Lugar,
                Start = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(appointment.FechaHora ?? DateTime.Now),
                    TimeZone = "America/Argentina/Buenos_Aires" // Ajustar según tu zona horaria
                },
                End = new EventDateTime
                {
                    DateTimeDateTimeOffset = new DateTimeOffset(appointment.FechaHora?.AddHours(2) ?? DateTime.Now.AddHours(2)), // Duración estimada de 2 horas
                    TimeZone = "America/Argentina/Buenos_Aires"
                },
                Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = new List<EventReminder>
                    {
                        new EventReminder
                        {
                            Method = "popup",
                            Minutes = 24 * 60 // 24 horas antes
                        },
                        new EventReminder
                        {
                            Method = "email", 
                            Minutes = 24 * 60 // 24 horas antes
                        }
                    }
                }
            };

            // Insertar el evento
            var request = calendarService.Events.Insert(calendarEvent, "primary");
            var createdEvent = await request.ExecuteAsync(ct);

            Console.WriteLine($"[CALENDAR] ✅ Event created with ID: {createdEvent.Id}");
            return createdEvent.Id;
        }, ct);
    }

    public async Task<bool> SendCalendarInviteAsync(
        string eventId, 
        string recipientEmail, 
        long chatId, 
        CancellationToken ct)
    {
        try
        {
            return await ExecuteWithAuthRetryAsync(chatId, async calendarService =>
            {
                // Obtener el evento existente
                var eventRequest = calendarService.Events.Get("primary", eventId);
                var existingEvent = await eventRequest.ExecuteAsync(ct);

                // Agregar el anestesiólogo como invitado
                if (existingEvent.Attendees == null)
                    existingEvent.Attendees = new List<EventAttendee>();

                existingEvent.Attendees.Add(new EventAttendee
                {
                    Email = recipientEmail,
                    ResponseStatus = "needsAction"
                });

                // Actualizar el evento
                var updateRequest = calendarService.Events.Update(existingEvent, "primary", eventId);
                updateRequest.SendUpdates = EventsResource.UpdateRequest.SendUpdatesEnum.All;
                
                await updateRequest.ExecuteAsync(ct);

                Console.WriteLine($"[CALENDAR] ✅ Invitation sent to: {recipientEmail}");
                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CALENDAR] ❌ Error sending calendar invite: {ex.Message}");
            return false;
        }
    }

    private string BuildEventTitle(Appointment appointment)
    {
        // Formato: "2 CERS" o "1 Amigdalectomía" 
        var cantidad = appointment.Cantidad ?? 1;
        var cirugia = appointment.Cirugia?.ToUpper() ?? "CIRUGÍA";
        var cirujano = appointment.Cirujano ?? "DESCONOCIDO";
        var lugar = appointment.Lugar ?? "LUGAR DESCONOCIDO";
        if (cantidad > 1 && !cirugia.EndsWith("S"))
        { cirugia += "S"; }

        return $"{cantidad} {cirugia} - {cirujano} - {lugar}";
    }

    private string BuildEventDescription(Appointment appointment)
    {
        var anesthesiologistLine = string.IsNullOrEmpty(appointment.Anestesiologo) ?
            "💉 Anestesiólogo: Sin asignar" :
            $"💉 Anestesiólogo: {appointment.Anestesiologo}";
            
        return $@"Detalles de la cirugía:

🏥 Lugar: {appointment.Lugar}
👨‍⚕️ Cirujano: {appointment.Cirujano}
🔬 Procedimiento: {appointment.Cirugia}
📊 Cantidad: {appointment.Cantidad}
{anesthesiologistLine}

📅 Fecha y hora: {appointment.FechaHora:dddd, dd MMMM yyyy HH:mm}

Evento creado automáticamente por RegistroCx Bot";
    }

    private async Task<CalendarService> GetAuthorizedCalendarServiceAsync(long chatId, CancellationToken ct)
    {
        // Obtener el perfil del usuario
        var userProfile = await _userRepo.GetAsync(chatId, ct);
        if (userProfile?.GoogleAccessToken == null)
        {
            throw new InvalidOperationException("El usuario no tiene acceso válido a Google Calendar. Por favor, vuelve a autorizar el acceso.");
        }

        // Verificar si el token ha expirado (si tenemos fecha de expiración)
        if (userProfile.GoogleTokenExpiry.HasValue && userProfile.GoogleTokenExpiry.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            Console.WriteLine($"[CALENDAR] Access token expired for chat {chatId}, refreshing...");
            
            if (string.IsNullOrWhiteSpace(userProfile.GoogleRefreshToken))
            {
                throw new InvalidOperationException("El token de acceso expiró y no hay token de renovación disponible. Por favor, vuelve a autorizar el acceso a Google Calendar.");
            }

            // Refrescar el token
            var tokenResponse = await _oauthService.RefreshAsync(userProfile.GoogleRefreshToken, ct);
            if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Error al renovar el token de acceso. Por favor, vuelve a autorizar el acceso a Google Calendar.");
            }

            Console.WriteLine($"[CALENDAR] ✅ Token refreshed successfully for chat {chatId}");
            
            // Actualizar los tokens del usuario
            await _userRepo.UpdateTokensAsync(
                chatId, 
                tokenResponse.AccessToken, 
                tokenResponse.RefreshToken, 
                tokenResponse.ExpiresAt?.DateTime, 
                ct);
        }

        // Crear el servicio de Google Calendar con el token (renovado si era necesario)
        var credential = GoogleCredential.FromAccessToken(userProfile.GoogleAccessToken);
        return new CalendarService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "RegistroCx Bot"
        });
    }

    private async Task<T> ExecuteWithAuthRetryAsync<T>(long chatId, Func<CalendarService, Task<T>> operation, CancellationToken ct)
    {
        try
        {
            var calendarService = await GetAuthorizedCalendarServiceAsync(chatId, ct);
            return await operation(calendarService);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.WriteLine($"[CALENDAR] 401 Unauthorized, forcing token refresh for chat {chatId}");
            
            // Forzar refresh del token
            var userProfile = await _userRepo.GetAsync(chatId, ct);
            if (userProfile?.GoogleRefreshToken == null)
            {
                throw new InvalidOperationException("La autenticación expiró y no hay token de renovación disponible. Por favor, vuelve a autorizar el acceso a Google Calendar.");
            }

            var tokenResponse = await _oauthService.RefreshAsync(userProfile.GoogleRefreshToken, ct);
            if (tokenResponse == null)
            {
                throw new InvalidOperationException("Error al renovar el token de autenticación. Por favor, vuelve a autorizar el acceso a Google Calendar.");
            }

            // Actualizar tokens en BD
            await _userRepo.UpdateTokensAsync(
                chatId, 
                tokenResponse.AccessToken, 
                tokenResponse.RefreshToken, 
                tokenResponse.ExpiresAt?.DateTime, 
                ct);

            Console.WriteLine($"[CALENDAR] ✅ Forced token refresh completed, retrying operation");

            // Retry la operación con el nuevo token
            var newCalendarService = await GetAuthorizedCalendarServiceAsync(chatId, ct);
            return await operation(newCalendarService);
        }
    }

    public async Task DeleteEventAsync(string eventId, long chatId, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"[CALENDAR] Deleting event {eventId} for chat {chatId}");

            await ExecuteWithAuthRetryAsync(chatId, async calendarService =>
            {
                // Eliminar el evento
                var deleteRequest = calendarService.Events.Delete("primary", eventId);
                await deleteRequest.ExecuteAsync(ct);

                Console.WriteLine($"[CALENDAR] ✅ Event {eventId} deleted successfully");
                return true; // Just a dummy return value
            }, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CALENDAR] ❌ Error deleting event {eventId}: {ex.Message}");
            throw;
        }
    }
}