using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using RegistroCx.Services.Repositories;
using RegistroCx.ProgramServices.Services.Telegram;

namespace RegistroCx.Services;

public class AppointmentReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppointmentReminderService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30); // Check every 30 minutes

    public AppointmentReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<AppointmentReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[REMINDER-SERVICE] Starting appointment reminder service");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndSendReminders(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REMINDER-SERVICE] Error occurred while checking reminders");
            }

            // Wait for next check interval
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("[REMINDER-SERVICE] Appointment reminder service stopped");
    }

    private async Task CheckAndSendReminders(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var appointmentRepo = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
        var botClient = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();

        _logger.LogInformation("[REMINDER-SERVICE] Checking for appointments needing reminders...");

        try
        {
            var appointmentsNeedingReminders = await appointmentRepo.GetAppointmentsNeedingRemindersAsync(ct);
            
            if (appointmentsNeedingReminders.Count == 0)
            {
                _logger.LogDebug("[REMINDER-SERVICE] No appointments needing reminders found");
                return;
            }

            _logger.LogInformation("[REMINDER-SERVICE] Found {Count} appointment(s) needing reminders", 
                appointmentsNeedingReminders.Count);

            foreach (var appointment in appointmentsNeedingReminders)
            {
                try
                {
                    await SendReminderNotification(botClient, appointment, ct);
                    await appointmentRepo.MarkReminderSentAsync(appointment.Id, ct);
                    
                    _logger.LogInformation("[REMINDER-SERVICE] ✅ Reminder sent for appointment ID {Id} - {Surgery} on {Date}",
                        appointment.Id, appointment.Cirugia, appointment.FechaHora);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[REMINDER-SERVICE] ❌ Failed to send reminder for appointment ID {Id}", 
                        appointment.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REMINDER-SERVICE] Error checking for reminders");
        }
    }

    private async Task SendReminderNotification(TelegramBotClient botClient, Models.Appointment appointment, CancellationToken ct)
    {
        if (!appointment.FechaHora.HasValue) return;
        
        var timeUntilSurgery = appointment.FechaHora.Value - DateTime.Now;
        var hoursUntil = (int)timeUntilSurgery.TotalHours;
        var minutesUntil = timeUntilSurgery.Minutes;

        var reminderMessage = $"⏰ RECORDATORIO DE CIRUGÍA\n\n" +
                             $"🏥 {appointment.Cantidad} {appointment.Cirugia?.ToUpper()}\n" +
                             $"📅 Fecha: {appointment.FechaHora:dddd, dd MMMM yyyy}\n" +
                             $"⌚ Hora: {appointment.FechaHora:HH:mm}\n" +
                             $"📍 Lugar: {appointment.Lugar}\n" +
                             $"👨‍⚕️ Cirujano: {appointment.Cirujano}\n" +
                             $"💉 Anestesiólogo: {appointment.Anestesiologo}\n\n";

        if (hoursUntil <= 1)
        {
            reminderMessage += $"🚨 ¡ATENCIÓN! La cirugía es en menos de {hoursUntil + 1} hora(s).";
        }
        else
        {
            reminderMessage += $"⏳ Faltan aproximadamente {hoursUntil} horas para la cirugía.";
        }

        reminderMessage += "\n\n" +
                          "📋 Recordá:\n" +
                          "• Llegar 30 min antes\n" +
                          "• Traer documentación\n" +
                          "• Confirmar con anestesiólogo\n\n" +
                          "¡Éxitos en la cirugía! 💪";

        if (appointment.ChatId.HasValue)
        {
            await MessageSender.SendWithRetry(
                appointment.ChatId.Value, 
                reminderMessage, 
                cancellationToken: ct);
        }
    }
}