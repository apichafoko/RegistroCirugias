using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RegistroCx.Services.Analytics
{
    public class ParsingAnalyticsService : IParsingAnalyticsService
    {
        private readonly ILogger<ParsingAnalyticsService> _logger;
        
        // In-memory storage for MVP - in production, use database
        private readonly List<ParsingLogEntry> _parsingLogs = new();
        private readonly List<PerformanceLogEntry> _performanceLogs = new();
        private readonly object _lock = new();

        public ParsingAnalyticsService(ILogger<ParsingAnalyticsService> logger)
        {
            _logger = logger;
        }

        public Task LogParsingErrorAsync(string errorType, string userInput, string? partialResult = null, CancellationToken ct = default)
        {
            var entry = new ParsingLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = "ERROR",
                ErrorType = errorType,
                UserInput = userInput,
                PartialResult = partialResult,
                Success = false
            };

            lock (_lock)
            {
                _parsingLogs.Add(entry);
                
                // Keep only last 1000 entries for memory management
                if (_parsingLogs.Count > 1000)
                {
                    _parsingLogs.RemoveRange(0, 100);
                }
            }

            _logger.LogWarning("Parsing Error: {ErrorType} - Input: {UserInput} - Partial: {PartialResult}", 
                errorType, userInput, partialResult);

            return Task.CompletedTask;
        }

        public Task LogParsingSuccessAsync(string userInput, Dictionary<string, string> parsedData, CancellationToken ct = default)
        {
            var entry = new ParsingLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = "SUCCESS",
                UserInput = userInput,
                ParsedData = parsedData,
                Success = true
            };

            lock (_lock)
            {
                _parsingLogs.Add(entry);
                
                if (_parsingLogs.Count > 1000)
                {
                    _parsingLogs.RemoveRange(0, 100);
                }
            }

            _logger.LogInformation("Parsing Success - Input: {UserInput} - Extracted: {FieldCount} fields", 
                userInput, parsedData?.Count ?? 0);

            return Task.CompletedTask;
        }

        public Task LogParsingWarningAsync(string warningType, string userInput, string details, CancellationToken ct = default)
        {
            var entry = new ParsingLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = "WARNING",
                ErrorType = warningType,
                UserInput = userInput,
                PartialResult = details,
                Success = true // Warnings are still considered successful parsing
            };

            lock (_lock)
            {
                _parsingLogs.Add(entry);
                
                if (_parsingLogs.Count > 1000)
                {
                    _parsingLogs.RemoveRange(0, 100);
                }
            }

            _logger.LogWarning("Parsing Warning: {WarningType} - Input: {UserInput} - Details: {Details}", 
                warningType, userInput, details);

            return Task.CompletedTask;
        }

        public Task TrackMissingFieldAsync(string fieldName, string userInput, CancellationToken ct = default)
        {
            var entry = new ParsingLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = "MISSING_FIELD",
                ErrorType = $"missing_{fieldName}",
                UserInput = userInput,
                MissingFields = new List<string> { fieldName },
                Success = false
            };

            lock (_lock)
            {
                _parsingLogs.Add(entry);
                
                if (_parsingLogs.Count > 1000)
                {
                    _parsingLogs.RemoveRange(0, 100);
                }
            }

            _logger.LogDebug("Missing Field: {FieldName} - Input: {UserInput}", fieldName, userInput);

            return Task.CompletedTask;
        }

        public Task TrackFieldExtractionAsync(string fieldName, string extractedValue, string userInput, CancellationToken ct = default)
        {
            _logger.LogDebug("Field Extracted: {FieldName} = {ExtractedValue} - Input: {UserInput}", 
                fieldName, extractedValue, userInput);

            return Task.CompletedTask;
        }

        public Task<List<ParsingPattern>> GetFrequentErrorPatternsAsync(TimeSpan? timeRange = null, CancellationToken ct = default)
        {
            var cutoffTime = timeRange.HasValue ? DateTime.UtcNow.Subtract(timeRange.Value) : DateTime.UtcNow.AddDays(-7);
            
            List<ParsingLogEntry> relevantLogs;
            lock (_lock)
            {
                relevantLogs = _parsingLogs
                    .Where(log => log.Timestamp >= cutoffTime && log.EventType == "ERROR")
                    .ToList();
            }

            var patterns = relevantLogs
                .GroupBy(log => log.ErrorType ?? "unknown")
                .Select(group => new ParsingPattern
                {
                    ErrorType = group.Key,
                    Pattern = ExtractPattern(group.Select(g => g.UserInput).ToList()),
                    Frequency = group.Count(),
                    LastOccurrence = group.Max(g => g.Timestamp),
                    ExampleInputs = group.Take(3).Select(g => g.UserInput).ToList()
                })
                .OrderByDescending(p => p.Frequency)
                .ToList();

            return Task.FromResult(patterns);
        }

        public Task<List<MissingFieldStats>> GetMissingFieldStatsAsync(TimeSpan? timeRange = null, CancellationToken ct = default)
        {
            var cutoffTime = timeRange.HasValue ? DateTime.UtcNow.Subtract(timeRange.Value) : DateTime.UtcNow.AddDays(-7);
            
            List<ParsingLogEntry> relevantLogs;
            lock (_lock)
            {
                relevantLogs = _parsingLogs
                    .Where(log => log.Timestamp >= cutoffTime)
                    .ToList();
            }

            var fieldStats = new Dictionary<string, MissingFieldStats>();
            var commonFields = new[] { "cirujano", "cirugia", "fecha", "hora", "lugar", "anestesiologo", "cantidad" };

            foreach (var field in commonFields)
            {
                var totalAttempts = relevantLogs.Count;
                var missingCount = relevantLogs.Count(log => 
                    log.MissingFields?.Contains(field) == true || 
                    log.ErrorType == $"missing_{field}");

                var commonPatterns = relevantLogs
                    .Where(log => log.MissingFields?.Contains(field) == true)
                    .GroupBy(log => ExtractSimplePattern(log.UserInput))
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => g.Key)
                    .ToList();

                fieldStats[field] = new MissingFieldStats
                {
                    FieldName = field,
                    MissingCount = missingCount,
                    TotalAttempts = totalAttempts,
                    CommonPatterns = commonPatterns
                };
            }

            return Task.FromResult(fieldStats.Values.OrderByDescending(s => s.MissingPercentage).ToList());
        }

        public Task<ParsingAnalytics> GetParsingAnalyticsAsync(long? chatId = null, TimeSpan? timeRange = null, CancellationToken ct = default)
        {
            var cutoffTime = timeRange.HasValue ? DateTime.UtcNow.Subtract(timeRange.Value) : DateTime.UtcNow.AddDays(-7);
            
            List<ParsingLogEntry> relevantLogs;
            List<PerformanceLogEntry> relevantPerfLogs;
            
            lock (_lock)
            {
                relevantLogs = _parsingLogs
                    .Where(log => log.Timestamp >= cutoffTime)
                    .ToList();
                    
                relevantPerfLogs = _performanceLogs
                    .Where(log => log.Timestamp >= cutoffTime)
                    .ToList();
            }

            var analytics = new ParsingAnalytics
            {
                TotalParsingAttempts = relevantLogs.Count,
                SuccessfulParsings = relevantLogs.Count(log => log.Success),
                FailedParsings = relevantLogs.Count(log => !log.Success),
                ErrorTypeCounts = relevantLogs
                    .Where(log => !string.IsNullOrEmpty(log.ErrorType))
                    .GroupBy(log => log.ErrorType!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                MissingFieldCounts = relevantLogs
                    .Where(log => log.MissingFields?.Any() == true)
                    .SelectMany(log => log.MissingFields!)
                    .GroupBy(field => field)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AverageParsingTime = relevantPerfLogs.Any() 
                    ? TimeSpan.FromMilliseconds(relevantPerfLogs.Average(log => log.Duration.TotalMilliseconds))
                    : TimeSpan.Zero,
                AnalysisPeriodStart = cutoffTime,
                AnalysisPeriodEnd = DateTime.UtcNow
            };

            return Task.FromResult(analytics);
        }

        public Task LogParsingPerformanceAsync(string operationType, TimeSpan duration, bool success, CancellationToken ct = default)
        {
            var entry = new PerformanceLogEntry
            {
                Timestamp = DateTime.UtcNow,
                OperationType = operationType,
                Duration = duration,
                Success = success
            };

            lock (_lock)
            {
                _performanceLogs.Add(entry);
                
                if (_performanceLogs.Count > 1000)
                {
                    _performanceLogs.RemoveRange(0, 100);
                }
            }

            if (duration.TotalSeconds > 5) // Log slow operations
            {
                _logger.LogWarning("Slow parsing operation: {OperationType} took {Duration}ms", 
                    operationType, duration.TotalMilliseconds);
            }

            return Task.CompletedTask;
        }

        public Task<List<PerformanceMetric>> GetPerformanceMetricsAsync(TimeSpan? timeRange = null, CancellationToken ct = default)
        {
            var cutoffTime = timeRange.HasValue ? DateTime.UtcNow.Subtract(timeRange.Value) : DateTime.UtcNow.AddDays(-7);
            
            List<PerformanceLogEntry> relevantLogs;
            lock (_lock)
            {
                relevantLogs = _performanceLogs
                    .Where(log => log.Timestamp >= cutoffTime)
                    .ToList();
            }

            var metrics = relevantLogs
                .GroupBy(log => log.OperationType)
                .Select(group => new PerformanceMetric
                {
                    OperationType = group.Key,
                    AverageDuration = TimeSpan.FromMilliseconds(group.Average(g => g.Duration.TotalMilliseconds)),
                    MaxDuration = group.Max(g => g.Duration),
                    MinDuration = group.Min(g => g.Duration),
                    TotalOperations = group.Count(),
                    SuccessfulOperations = group.Count(g => g.Success)
                })
                .OrderByDescending(m => m.AverageDuration)
                .ToList();

            return Task.FromResult(metrics);
        }

        // Helper methods
        private string ExtractPattern(List<string> inputs)
        {
            if (!inputs.Any()) return "unknown";
            
            // Simple pattern extraction - look for common words or structures
            var commonWords = inputs
                .SelectMany(input => input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(word => word.ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();

            return commonWords.Any() ? string.Join(", ", commonWords) : "various_patterns";
        }

        private string ExtractSimplePattern(string input)
        {
            if (string.IsNullOrEmpty(input)) return "empty";
            
            var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 3) return "short_input";
            if (words.Length > 10) return "long_input";
            return "medium_input";
        }

        // Internal data structures
        private class ParsingLogEntry
        {
            public DateTime Timestamp { get; set; }
            public string EventType { get; set; } = string.Empty; // ERROR, SUCCESS, WARNING, MISSING_FIELD
            public string? ErrorType { get; set; }
            public string UserInput { get; set; } = string.Empty;
            public string? PartialResult { get; set; }
            public Dictionary<string, string>? ParsedData { get; set; }
            public List<string>? MissingFields { get; set; }
            public bool Success { get; set; }
        }

        private class PerformanceLogEntry
        {
            public DateTime Timestamp { get; set; }
            public string OperationType { get; set; } = string.Empty;
            public TimeSpan Duration { get; set; }
            public bool Success { get; set; }
        }
    }
}