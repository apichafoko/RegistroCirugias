using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RegistroCx.Services.Analytics
{
    public interface IParsingAnalyticsService
    {
        // Logging de errores de parsing
        Task LogParsingErrorAsync(string errorType, string userInput, string? partialResult = null, CancellationToken ct = default);
        Task LogParsingSuccessAsync(string userInput, Dictionary<string, string> parsedData, CancellationToken ct = default);
        Task LogParsingWarningAsync(string warningType, string userInput, string details, CancellationToken ct = default);
        
        // Tracking de campos faltantes
        Task TrackMissingFieldAsync(string fieldName, string userInput, CancellationToken ct = default);
        Task TrackFieldExtractionAsync(string fieldName, string extractedValue, string userInput, CancellationToken ct = default);
        
        // Análisis de patrones
        Task<List<ParsingPattern>> GetFrequentErrorPatternsAsync(TimeSpan? timeRange = null, CancellationToken ct = default);
        Task<List<MissingFieldStats>> GetMissingFieldStatsAsync(TimeSpan? timeRange = null, CancellationToken ct = default);
        Task<ParsingAnalytics> GetParsingAnalyticsAsync(long? chatId = null, TimeSpan? timeRange = null, CancellationToken ct = default);
        
        // Performance tracking
        Task LogParsingPerformanceAsync(string operationType, TimeSpan duration, bool success, CancellationToken ct = default);
        Task<List<PerformanceMetric>> GetPerformanceMetricsAsync(TimeSpan? timeRange = null, CancellationToken ct = default);
    }

    public class ParsingPattern
    {
        public string ErrorType { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public DateTime LastOccurrence { get; set; }
        public List<string> ExampleInputs { get; set; } = new();
    }

    public class MissingFieldStats
    {
        public string FieldName { get; set; } = string.Empty;
        public int MissingCount { get; set; }
        public int TotalAttempts { get; set; }
        public double MissingPercentage => TotalAttempts > 0 ? (double)MissingCount / TotalAttempts * 100 : 0;
        public List<string> CommonPatterns { get; set; } = new();
    }

    public class ParsingAnalytics
    {
        public int TotalParsingAttempts { get; set; }
        public int SuccessfulParsings { get; set; }
        public int FailedParsings { get; set; }
        public double SuccessRate => TotalParsingAttempts > 0 ? (double)SuccessfulParsings / TotalParsingAttempts * 100 : 0;
        public Dictionary<string, int> ErrorTypeCounts { get; set; } = new();
        public Dictionary<string, int> MissingFieldCounts { get; set; } = new();
        public TimeSpan AverageParsingTime { get; set; }
        public DateTime AnalysisPeriodStart { get; set; }
        public DateTime AnalysisPeriodEnd { get; set; }
    }

    public class PerformanceMetric
    {
        public string OperationType { get; set; } = string.Empty;
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
    }
}