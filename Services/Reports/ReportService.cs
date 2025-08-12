using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using RegistroCx.Models.ReportModels;
using RegistroCx.ProgramServices.Services.Telegram;
using RegistroCx.Services;

namespace RegistroCx.Services.Reports;

public class ReportService : IReportService
{
    private readonly ReportDataService _dataService;
    private readonly PdfGeneratorService _pdfGenerator;
    
    // Estados para manejo de comandos con parámetros (compartido entre requests)
    private readonly Dictionary<long, ReportCommandState> _commandStates;

    public ReportService(ReportDataService dataService, PdfGeneratorService pdfGenerator, Dictionary<long, ReportCommandState> commandStates)
    {
        _dataService = dataService;
        _pdfGenerator = pdfGenerator;
        _commandStates = commandStates;
    }

    public async Task<bool> HandleReportCommandAsync(ITelegramBotClient bot, long chatId, string command, CancellationToken ct)
    {
        Console.WriteLine($"[REPORT] HandleReportCommandAsync called with: '{command}' for chat {chatId}");
        var commandLower = command.Trim().ToLowerInvariant();
        Console.WriteLine($"[REPORT] Command lowercased: '{commandLower}'.");
        
        switch (commandLower)
        {
            case "/semanal":
                await HandleWeeklyReportCommand(bot, chatId, ct);
                return true;
                
            case "/mensual":
                Console.WriteLine($"[REPORT] Calling HandleMonthlyReportCommand for chat {chatId}");
                try
                {
                    await HandleMonthlyReportCommand(bot, chatId, ct);
                    Console.WriteLine($"[REPORT] HandleMonthlyReportCommand completed successfully for chat {chatId}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[REPORT] Error in HandleMonthlyReportCommand: {ex.Message}");
                    Console.WriteLine($"[REPORT] StackTrace: {ex.StackTrace}");
                    throw;
                }
                
            case "/anual":
                await HandleAnnualReportCommand(bot, chatId, ct);
                return true;
                
            default:
                // Verificar si es una respuesta a un comando pendiente
                return await HandlePendingReportCommand(bot, chatId, command, ct);
        }
    }

    private async Task HandleWeeklyReportCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        try
        {
            await MessageSender.SendWithRetry(chatId, "⏳ Procesando...", cancellationToken: ct);
            
            await MessageSender.SendWithRetry(chatId, 
                "📊 Generando reporte semanal (últimos 7 días)...", 
                cancellationToken: ct);

            var pdfPath = await GenerateWeeklyReportAsync(chatId, ct);
            await SendPdfReport(bot, chatId, pdfPath, "Reporte Semanal", ct);
            
            // Limpiar archivo temporal
            if (System.IO.File.Exists(pdfPath))
                System.IO.File.Delete(pdfPath);
                
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REPORT] Error generating weekly report: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error generando el reporte semanal. Intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    private async Task HandleMonthlyReportCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        Console.WriteLine($"[REPORT] HandleMonthlyReportCommand started for chat {chatId}");
        
        try
        {
            var state = new ReportCommandState
            {
                Type = ReportType.Monthly,
                WaitingFor = "month_year"
            };
            
            Console.WriteLine($"[REPORT] Created state: Type={state.Type}, WaitingFor={state.WaitingFor}");
            
            _commandStates[chatId] = state;
            Console.WriteLine($"[REPORT] Set state for chat {chatId}: Type={state.Type}, WaitingFor={state.WaitingFor}, Total states: {_commandStates.Count}");

            await MessageSender.SendWithRetry(chatId,
                "📅 ¿De qué mes querés el reporte?\n\n" +
                "Enviame en formato **MM/YYYY** (ej: 03/2024, 12/2023)",
                cancellationToken: ct);
                
            Console.WriteLine($"[REPORT] Message sent successfully to chat {chatId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REPORT] Exception in HandleMonthlyReportCommand: {ex.Message}");
            Console.WriteLine($"[REPORT] StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    private async Task HandleAnnualReportCommand(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        _commandStates[chatId] = new ReportCommandState
        {
            Type = ReportType.Annual,
            WaitingFor = "year"
        };

        await MessageSender.SendWithRetry(chatId,
            "📅 ¿De qué año querés el reporte?\n\n" +
            "Enviame el año (ej: **2024**, **2023**)",
            cancellationToken: ct);
    }

    private async Task<bool> HandlePendingReportCommand(ITelegramBotClient bot, long chatId, string input, CancellationToken ct)
    {
        Console.WriteLine($"[REPORT] HandlePendingReportCommand called with: '{input}' for chat {chatId}");
        if (!_commandStates.TryGetValue(chatId, out var state))
        {
            Console.WriteLine($"[REPORT] No pending state found for chat {chatId}");
            return false;
        }
        Console.WriteLine($"[REPORT] Found pending state - Type: {state.Type}, WaitingFor: {state.WaitingFor}");

        switch (state.WaitingFor)
        {
            case "month_year":
                return await HandleMonthYearInput(bot, chatId, input, ct);
                
            case "year":
                return await HandleYearInput(bot, chatId, input, ct);
                
            default:
                _commandStates.Remove(chatId);
                return false;
        }
    }

    private async Task<bool> HandleMonthYearInput(ITelegramBotClient bot, long chatId, string input, CancellationToken ct)
    {
        // Validar formato MM/YYYY
        var match = Regex.Match(input.Trim(), @"^(\d{1,2})/(\d{4})$");
        if (!match.Success)
        {
            await MessageSender.SendWithRetry(chatId,
                "❌ Formato inválido. Usa MM/YYYY (ej: 03/2024)",
                cancellationToken: ct);
            return true; // Mantener el estado para otro intento
        }

        var month = int.Parse(match.Groups[1].Value);
        var year = int.Parse(match.Groups[2].Value);

        // Validar valores
        if (month < 1 || month > 12)
        {
            await MessageSender.SendWithRetry(chatId,
                "❌ Mes inválido. Debe ser entre 01 y 12.",
                cancellationToken: ct);
            return true;
        }

        if (year < 2020 || year > DateTime.Now.Year + 1)
        {
            await MessageSender.SendWithRetry(chatId,
                "❌ Año inválido. Debe ser entre 2020 y el año actual.",
                cancellationToken: ct);
            return true;
        }

        try
        {
            await MessageSender.SendWithRetry(chatId, "⏳ Procesando...", cancellationToken: ct);
            
            var monthName = ReportPeriod.CreateMonthly(month, year).DisplayName;
            await MessageSender.SendWithRetry(chatId,
                $"📊 Generando reporte mensual ({monthName})...",
                cancellationToken: ct);

            var pdfPath = await GenerateMonthlyReportAsync(chatId, month, year, ct);
            await SendPdfReport(bot, chatId, pdfPath, $"Reporte Mensual - {monthName}", ct);
            
            // Limpiar archivo temporal
            if (System.IO.File.Exists(pdfPath))
                System.IO.File.Delete(pdfPath);
                
            // Reporte exitoso - limpiar estado
            _commandStates.Remove(chatId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REPORT] Error generating monthly report: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error generando el reporte mensual. Intenta nuevamente.",
                cancellationToken: ct);
            // NO remover estado en caso de error para permitir reintento
        }

        return true;
    }

    private async Task<bool> HandleYearInput(ITelegramBotClient bot, long chatId, string input, CancellationToken ct)
    {
        if (!int.TryParse(input.Trim(), out var year))
        {
            await MessageSender.SendWithRetry(chatId,
                "❌ Año inválido. Debe ser un número (ej: 2024)",
                cancellationToken: ct);
            return true; // Mantener el estado para otro intento
        }

        // Validar rango de años
        if (year < 2020 || year > DateTime.Now.Year + 1)
        {
            await MessageSender.SendWithRetry(chatId,
                "❌ Año inválido. Debe ser entre 2020 y el año actual.",
                cancellationToken: ct);
            return true;
        }

        try
        {
            await MessageSender.SendWithRetry(chatId, "⏳ Procesando...", cancellationToken: ct);
            
            await MessageSender.SendWithRetry(chatId,
                $"📊 Generando reporte anual ({year})...",
                cancellationToken: ct);

            var pdfPath = await GenerateAnnualReportAsync(chatId, year, ct);
            await SendPdfReport(bot, chatId, pdfPath, $"Reporte Anual - {year}", ct);
            
            // Limpiar archivo temporal
            if (System.IO.File.Exists(pdfPath))
                System.IO.File.Delete(pdfPath);
                
            // Reporte exitoso - limpiar estado
            _commandStates.Remove(chatId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REPORT] Error generating annual report: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "❌ Error generando el reporte anual. Intenta nuevamente.",
                cancellationToken: ct);
            // NO remover estado en caso de error para permitir reintento
        }

        return true;
    }

    private async Task SendPdfReport(ITelegramBotClient bot, long chatId, string pdfPath, string reportName, CancellationToken ct)
    {
        try
        {
            var fileName = Path.GetFileName(pdfPath);
            await using var stream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read);

            await bot.SendDocument(
                chatId: chatId,
                document: InputFile.FromStream(stream, fileName),
                caption: $"✅ {reportName} generado exitosamente",
                cancellationToken: ct
            );

            Console.WriteLine($"[REPORT] PDF sent successfully: {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REPORT] Error sending PDF: {ex}");
            await MessageSender.SendWithRetry(chatId,
                "✅ Reporte generado pero no se pudo enviar el archivo. Intenta nuevamente.",
                cancellationToken: ct);
        }
    }

    public async Task<string> GenerateWeeklyReportAsync(long chatId, CancellationToken ct = default)
    {
        var reportData = await _dataService.GenerateWeeklyReportDataAsync(chatId, ct);
        return await _pdfGenerator.CreateWeeklyReportPdfAsync(reportData, ct);
    }

    public async Task<string> GenerateMonthlyReportAsync(long chatId, int month, int year, CancellationToken ct = default)
    {
        var reportData = await _dataService.GenerateMonthlyReportDataAsync(chatId, month, year, ct);
        return await _pdfGenerator.CreateMonthlyReportPdfAsync(reportData, ct);
    }

    public async Task<string> GenerateAnnualReportAsync(long chatId, int year, CancellationToken ct = default)
    {
        var reportData = await _dataService.GenerateAnnualReportDataAsync(chatId, year, ct);
        return await _pdfGenerator.CreateAnnualReportPdfAsync(reportData, ct);
    }

    public class ReportCommandState
    {
        public ReportType Type { get; set; }
        public string WaitingFor { get; set; } = string.Empty;
    }
}