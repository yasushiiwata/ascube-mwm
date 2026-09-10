using Ascube.Mwm.Abstractions;

namespace Ascube.Mwm.Core.Tests.Matching;

/// <summary>
/// T5（MatchEngine）の受入条件。1人モデルなので判定対象は常に0件か1件の候補。
/// </summary>
public class MatchEngineTests
{
    private static WorkItemView MakeCandidate(
        string scheduledDate = "20260910",
        string patientId = "000012345678",
        string? familyKanji = "アスキューブ",
        string? givenKanji = "タロウ",
        string? familyKana = "ｱｽｷｭｰﾌﾞ",
        string? givenKana = "ﾀﾛｳ") =>
        new()
        {
            WorkItemId = "wi-1",
            StudyInstanceUid = "2.25.1",
            StablePatientId = patientId,
            FamilyNameKanji = familyKanji,
            GivenNameKanji = givenKanji,
            FamilyNameKana = familyKana,
            GivenNameKana = givenKana,
            ScheduledDate = scheduledDate,
        };

    // --- Range Matching ---

    [Theory]
    [InlineData("20260910", "20260904-20260918")] // 範囲内
    [InlineData("20260904", "20260904-20260918")] // 下端
    [InlineData("20260918", "20260904-20260918")] // 上端
    [InlineData("20260910", "-20260918")]          // 開始側開放
    [InlineData("20260910", "20260904-")]          // 終了側開放
    [InlineData("20260910", "20260910")]           // 厳密一致
    [InlineData("20260910", "")]                   // Universal
    [InlineData("20260910", null)]                 // Universal（null）
    public void Evaluate_ScheduledDateInRange_Matches(string scheduledDate, string? range)
    {
        var candidate = MakeCandidate(scheduledDate: scheduledDate);
        var criteria = new QueryCriteria { ScheduledDateRange = range };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.True(result.IsMatch);
        Assert.Equal(MatchOutcome.Matched, result.Outcome);
    }

    [Theory]
    [InlineData("20260903", "20260904-20260918")] // 下限より前
    [InlineData("20260919", "20260904-20260918")] // 上限より後
    [InlineData("20260919", "-20260918")]          // 開始側開放・上限超え
    [InlineData("20260903", "20260904-")]          // 終了側開放・下限未満
    [InlineData("20260911", "20260910")]           // 厳密一致・不一致
    public void Evaluate_ScheduledDateOutOfRange_DoesNotMatch(string scheduledDate, string range)
    {
        var candidate = MakeCandidate(scheduledDate: scheduledDate);
        var criteria = new QueryCriteria { ScheduledDateRange = range };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.False(result.IsMatch);
        Assert.Equal(MatchOutcome.ScheduledDateOutOfRange, result.Outcome);
    }

    [Fact]
    public void Evaluate_WideDaysBackForwardRange_StillOnlyMatchesCurrentEntryDate()
    {
        // 「Days Back 60 / Forward 2 の広い範囲を要求しても、返るのは CurrentEntry の1件だけ」
        var candidate = MakeCandidate(scheduledDate: "20260910");
        var wideRange = new QueryCriteria { ScheduledDateRange = "20260711-20260912" }; // -60日 〜 +2日

        var result = MatchEngine.Evaluate(wideRange, candidate);

        Assert.True(result.IsMatch); // 範囲内なのでこの1件は一致する（他の候補は存在しない＝1人モデル）
    }

    // --- Wildcard Matching ---

    [Theory]
    [InlineData("000012345678", "000012345678")]
    [InlineData("0000*", "000012345678")]
    [InlineData("*345678", "000012345678")]
    [InlineData("000012?45678", "000012345678")]
    [InlineData("*", "000012345678")]
    [InlineData("", "000012345678")]
    [InlineData(null, "000012345678")]
    public void Evaluate_PatientIdMatches(string? requestedPatientId, string candidatePatientId)
    {
        var candidate = MakeCandidate(patientId: candidatePatientId);
        var criteria = new QueryCriteria { PatientId = requestedPatientId };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.True(result.IsMatch);
    }

    [Theory]
    [InlineData("000099999999")]
    [InlineData("0001*999")]
    public void Evaluate_PatientIdMismatch_DoesNotMatch(string requestedPatientId)
    {
        var candidate = MakeCandidate(patientId: "000012345678");
        var criteria = new QueryCriteria { PatientId = requestedPatientId };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.False(result.IsMatch);
        Assert.Equal(MatchOutcome.PatientIdMismatch, result.Outcome);
    }

    [Theory]
    [InlineData("アスキューブ^タロウ")]
    [InlineData("アスキューブ*")]
    [InlineData("ｱｽｷｭｰﾌﾞ^ﾀﾛｳ")] // カナ側でも一致する
    public void Evaluate_PatientNameMatches(string requestedName)
    {
        var candidate = MakeCandidate();
        var criteria = new QueryCriteria { PatientName = requestedName };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Evaluate_PatientNameMismatch_DoesNotMatch()
    {
        var candidate = MakeCandidate();
        var criteria = new QueryCriteria { PatientName = "ヤマダ^ハナコ" };

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.False(result.IsMatch);
        Assert.Equal(MatchOutcome.PatientNameMismatch, result.Outcome);
    }

    [Fact]
    public void Evaluate_UnspecifiedCriteria_AlwaysMatches()
    {
        var candidate = MakeCandidate();
        var criteria = new QueryCriteria();

        var result = MatchEngine.Evaluate(criteria, candidate);

        Assert.True(result.IsMatch);
    }

    // --- Modality Matching ---

    [Theory]
    [InlineData("ignore", "OT", "")]     // ignore：常に一致
    [InlineData("ignore", "", "BMD")]
    [InlineData("strict", "BMD", "BMD")] // strict：完全一致
    [InlineData("strict", "", "BMD")]    // strict：要求が空＝Universal
    [InlineData("lenient", "OT", "")]    // lenient：候補側未設定なら判定不能として一致扱い
    public void EvaluateModality_Matches(string mode, string requested, string candidateModality)
    {
        Assert.True(MatchEngine.EvaluateModality(requested, candidateModality, mode));
    }

    [Theory]
    [InlineData("strict", "OT", "")]    // strict：候補側未設定は不一致扱い
    [InlineData("strict", "OT", "BMD")] // strict：値が違う
    [InlineData("lenient", "OT", "BMD")] // lenient でも候補に値があれば厳密比較
    public void EvaluateModality_DoesNotMatch(string mode, string requested, string candidateModality)
    {
        Assert.False(MatchEngine.EvaluateModality(requested, candidateModality, mode));
    }
}
