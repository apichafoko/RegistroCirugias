using System;

namespace RegistroCx.Domain;

public class UserCustomTerm
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public string UserTerm { get; set; } = string.Empty;      // "cataratas", "faco"
    public string StandardTerm { get; set; } = string.Empty;  // "FACOEMULSIFICACION"
    public string TermType { get; set; } = string.Empty;      // "surgery", "surgeon", "place"
    public int Frequency { get; set; } = 1;                   // Cuántas veces lo usó
    public decimal Confidence { get; set; } = 0.5m;           // Confianza 0.0 - 1.0
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class UserCommunicationPattern
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public string PatternType { get; set; } = string.Empty;   // "frequent_surgery", "typical_surgeon"
    public string PatternValue { get; set; } = string.Empty;  // JSON o string
    public int Frequency { get; set; } = 1;
    public decimal Confidence { get; set; } = 0.5m;
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Enums para tipos
public static class TermTypes
{
    public const string Surgery = "surgery";
    public const string Surgeon = "surgeon";
    public const string Place = "place";
    public const string Anesthesiologist = "anesthesiologist";
}

public static class PatternTypes
{
    public const string FrequentSurgery = "frequent_surgery";
    public const string TypicalSurgeon = "typical_surgeon";
    public const string PreferredPlace = "preferred_place";
    public const string UsualQuantity = "usual_quantity";
}