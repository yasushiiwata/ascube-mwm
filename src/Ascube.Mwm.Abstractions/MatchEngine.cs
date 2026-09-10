using System.Globalization;
using System.Text.RegularExpressions;

namespace Ascube.Mwm.Abstractions;

/// <summary>
/// DICOM C-FIND のマッチング判定（実装指示書 v2 §5-T5）。1人モデルでは候補は常に0件か1件。
/// fo-dicom にも SQLite にも依存しない純粋な文字列処理として Abstractions に置く
/// （<see cref="Ascube.Mwm.Store"/> と Ascube.Mwm.Scp の両方から使うため。BRIDGE-Navi 本体は
/// Abstractions しか参照しないので、ここに fo-dicom 型を持ち込んではいけない）。
/// </summary>
public static class MatchEngine
{
    /// <summary>
    /// 日付範囲・PatientID・PatientName を判定する。Modality はプロファイル設定（候補側の値）が要るため
    /// <see cref="EvaluateModality"/> で別途判定する。
    /// </summary>
    public static MatchResult Evaluate(QueryCriteria criteria, WorkItemView candidate)
    {
        if (!string.IsNullOrEmpty(criteria.ScheduledDateRange)
            && !IsDateInRange(criteria.ScheduledDateRange, candidate.ScheduledDate))
        {
            return new MatchResult(false, MatchOutcome.ScheduledDateOutOfRange);
        }

        if (!string.IsNullOrEmpty(criteria.PatientId)
            && !IsWildcardMatch(criteria.PatientId, candidate.StablePatientId))
        {
            return new MatchResult(false, MatchOutcome.PatientIdMismatch);
        }

        if (!string.IsNullOrEmpty(criteria.PatientName) && !MatchesAnyPatientName(criteria.PatientName, candidate))
        {
            return new MatchResult(false, MatchOutcome.PatientNameMismatch);
        }

        return new MatchResult(true, MatchOutcome.Matched);
    }

    /// <summary>
    /// Modality の判定。候補側の値は DB ではなくプロファイル設定（dataset.elements の const: 値）由来のため、
    /// 呼び出し側（Ascube.Mwm.Scp）が渡す。modalityMatching の既定は "ignore"（APEX の Modality 既定は None）。
    /// strict: 候補側が未設定なら不一致扱い。lenient: 候補側が未設定なら判定不能として一致扱いにする。
    /// </summary>
    public static bool EvaluateModality(string? requested, string? candidateModality, string modalityMatching)
    {
        if (modalityMatching == "ignore" || string.IsNullOrEmpty(requested))
        {
            return true;
        }

        if (string.IsNullOrEmpty(candidateModality))
        {
            return modalityMatching == "lenient";
        }

        return IsWildcardMatch(requested, candidateModality);
    }

    /// <summary>
    /// DICOM Range Matching（"YYYYMMDD-YYYYMMDD"。片側を省略すると開放）。
    /// ダッシュを含まなければ厳密一致（exact）。空/null は Universal Matching（常に true）。
    /// </summary>
    public static bool IsDateInRange(string? rangeExpression, string candidateDateYyyymmdd)
    {
        if (string.IsNullOrEmpty(rangeExpression))
        {
            return true;
        }

        var candidate = ParseDate(candidateDateYyyymmdd);

        var dashIndex = rangeExpression.IndexOf('-');
        if (dashIndex < 0)
        {
            return ParseDate(rangeExpression) == candidate;
        }

        var startPart = rangeExpression[..dashIndex];
        var endPart = rangeExpression[(dashIndex + 1)..];

        if (startPart.Length > 0 && candidate < ParseDate(startPart))
        {
            return false;
        }

        if (endPart.Length > 0 && candidate > ParseDate(endPart))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// DICOM Wildcard Matching（"*" は任意長の任意文字列、"?" は任意の1文字）。
    /// 大文字小文字を区別する（Ordinal）。空/null は Universal Matching（常に true）。
    /// </summary>
    public static bool IsWildcardMatch(string? pattern, string? value)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        value ??= string.Empty;

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return string.Equals(pattern, value, StringComparison.Ordinal);
        }

        var regexPattern = "^" + string.Concat(pattern.Select(ToRegexFragment)) + "$";
        return Regex.IsMatch(value, regexPattern);

        static string ToRegexFragment(char c) => c switch
        {
            '*' => ".*",
            '?' => ".",
            _ => Regex.Escape(c.ToString()),
        };
    }

    private static bool MatchesAnyPatientName(string pattern, WorkItemView candidate)
    {
        var kanji = ComposeName(candidate.FamilyNameKanji, candidate.GivenNameKanji);
        var kana = ComposeName(candidate.FamilyNameKana, candidate.GivenNameKana);

        return (kanji is not null && IsWildcardMatch(pattern, kanji))
            || (kana is not null && IsWildcardMatch(pattern, kana));
    }

    private static string? ComposeName(string? family, string? given) =>
        string.IsNullOrEmpty(family) && string.IsNullOrEmpty(given) ? null : $"{family}^{given}";

    private static DateOnly ParseDate(string yyyymmdd) =>
        DateOnly.ParseExact(yyyymmdd, "yyyyMMdd", CultureInfo.InvariantCulture);
}
