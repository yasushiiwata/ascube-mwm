namespace Ascube.Mwm.Core.Config;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues)
{
    public static ValidationResult Ok() => new(true, Array.Empty<ValidationIssue>());

    public static ValidationResult Fail(IReadOnlyList<ValidationIssue> issues) => new(false, issues);
}
