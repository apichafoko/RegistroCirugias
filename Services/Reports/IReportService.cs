using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;

namespace RegistroCx.Services.Reports;

public interface IReportService
{
    Task<bool> HandleReportCommandAsync(ITelegramBotClient bot, long chatId, string command, CancellationToken ct);
    Task<string> GenerateWeeklyReportAsync(long chatId, CancellationToken ct = default);
    Task<string> GenerateMonthlyReportAsync(long chatId, int month, int year, CancellationToken ct = default);
    Task<string> GenerateAnnualReportAsync(long chatId, int year, CancellationToken ct = default);
}