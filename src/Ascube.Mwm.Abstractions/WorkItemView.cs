namespace Ascube.Mwm.Abstractions;

/// <summary>SCP が読み取る、StudyInstanceUID 採番済みの受診者1件。実装指示書 v2 §3-3。</summary>
public sealed record WorkItemView
{
    public required string WorkItemId { get; init; }

    public required string StudyInstanceUid { get; init; }

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
