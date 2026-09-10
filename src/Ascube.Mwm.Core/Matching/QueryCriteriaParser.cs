using Ascube.Mwm.Abstractions;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Matching;

/// <summary>
/// C-FIND 要求の <see cref="DicomDataset"/> から <see cref="QueryCriteria"/> を取り出す（実装指示書 v2 T5）。
/// fo-dicom に依存するため Abstractions ではなく Core に置く。
/// </summary>
public static class QueryCriteriaParser
{
    public static QueryCriteria Parse(DicomDataset requestDataset)
    {
        string? scheduledDateRange = null;
        string? modality = null;

        if (requestDataset.TryGetSequence(DicomTag.ScheduledProcedureStepSequence, out var sps) && sps.Items.Count > 0)
        {
            var item = sps.Items[0];
            scheduledDateRange = NullIfEmpty(item.GetSingleValueOrDefault(DicomTag.ScheduledProcedureStepStartDate, string.Empty));
            modality = NullIfEmpty(item.GetSingleValueOrDefault(DicomTag.Modality, string.Empty));
        }

        return new QueryCriteria
        {
            ScheduledDateRange = scheduledDateRange,
            PatientId = NullIfEmpty(requestDataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty)),
            PatientName = NullIfEmpty(requestDataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty)),
            Modality = modality,
        };
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
