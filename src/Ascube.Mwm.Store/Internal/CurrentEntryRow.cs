using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Store.Internal;

/// <summary>CurrentEntry ＋ WorkItem ＋ Patient ＋ UidAllocation を結合した生の1行。</summary>
internal sealed record CurrentEntryRow(
    string WorkItemId,
    DateTimeOffset SetAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? StudyInstanceUid,
    string StablePatientId,
    string ScheduledDate,
    string? AccessionNumber,
    string? RequestedProcedureId,
    string? RequestedProcedureDesc,
    double? PatientSizeM,
    double? PatientWeightKg,
    string? SourceMessageId,
    string? FamilyNameKanji,
    string? GivenNameKanji,
    string? FamilyNameKana,
    string? GivenNameKana,
    string? BirthDate,
    Sex Sex);
