namespace Ascube.Mwm.Core.Config;

/// <summary>検証で見つかった1件の問題。<see cref="Path"/> は JSON Pointer 風の位置表現（例: $.dataset.elements[2].tag）。</summary>
public sealed record ValidationIssue(string Path, string Message);
