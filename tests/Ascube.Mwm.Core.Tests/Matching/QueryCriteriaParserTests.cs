using Ascube.Mwm.Core.Matching;
using FellowOakDicom;

namespace Ascube.Mwm.Core.Tests.Matching;

public class QueryCriteriaParserTests
{
    [Fact]
    public void Parse_EmptyDataset_AllCriteriaAreNull()
    {
        var criteria = QueryCriteriaParser.Parse(new DicomDataset());

        Assert.Null(criteria.ScheduledDateRange);
        Assert.Null(criteria.PatientId);
        Assert.Null(criteria.PatientName);
        Assert.Null(criteria.Modality);
    }

    [Fact]
    public void Parse_TopLevelKeys_ReadPatientIdAndName()
    {
        var dataset = new DicomDataset
        {
            { DicomTag.PatientID, "000012345678" },
            { DicomTag.PatientName, "アスキューブ^タロウ" },
        };

        var criteria = QueryCriteriaParser.Parse(dataset);

        Assert.Equal("000012345678", criteria.PatientId);
        Assert.Equal("アスキューブ^タロウ", criteria.PatientName);
    }

    [Fact]
    public void Parse_ScheduledProcedureStepSequence_ReadsDateRangeAndModality()
    {
        var sps = new DicomDataset
        {
            { DicomTag.ScheduledProcedureStepStartDate, "20260904-20260918" },
            { DicomTag.Modality, "BMD" },
        };
        var dataset = new DicomDataset
        {
            new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps),
        };

        var criteria = QueryCriteriaParser.Parse(dataset);

        Assert.Equal("20260904-20260918", criteria.ScheduledDateRange);
        Assert.Equal("BMD", criteria.Modality);
    }

    [Fact]
    public void Parse_EmptySequenceItem_LeavesDateAndModalityNull()
    {
        var dataset = new DicomDataset
        {
            new DicomSequence(DicomTag.ScheduledProcedureStepSequence, new DicomDataset()),
        };

        var criteria = QueryCriteriaParser.Parse(dataset);

        Assert.Null(criteria.ScheduledDateRange);
        Assert.Null(criteria.Modality);
    }
}
