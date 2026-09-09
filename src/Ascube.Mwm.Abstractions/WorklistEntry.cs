namespace Ascube.Mwm.Abstractions;

/// <summary>
/// この端末に「今」載せる受診者1人分。実装指示書 v2 §3-1。
/// </summary>
public sealed record WorklistEntry
{
    public required string StablePatientId { get; init; }

    public string? FamilyNameKanji { get; init; }

    public string? GivenNameKanji { get; init; }

    public string? FamilyNameKana { get; init; }

    public string? GivenNameKana { get; init; }

    public string? BirthDate { get; init; }

    public Sex Sex { get; init; }

    public required string ScheduledDate { get; init; }

    public string? AccessionNumber { get; init; }

    public string? RequestedProcedureId { get; init; }

    public string? RequestedProcedureDesc { get; init; }

    public double? PatientSizeM { get; init; }

    public double? PatientWeightKg { get; init; }

    public string? SourceMessageId { get; init; }
}
