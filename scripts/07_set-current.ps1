<#
.SYNOPSIS
  受診者をワークリストに1人セットする（mwm-admin admin set-current の薄いラッパー）。

.DESCRIPTION
  BRIDGE-Navi との結線がまだ無い、または現地で不調なときに、SCP とダミー装置の間だけで
  疎通を証明するための開発用コマンド。本番の受診者データ投入経路ではない
  （docs/連携テスト手順.md §2）。実行すると SqliteWorklistWriter.SetCurrentAsync を直接呼ぶ。

.PARAMETER PatientId
  依頼電文 項番3「個人番号」12桁。★項番4「受診番号」ではない（規則18）。
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$DbPath = (Join-Path $PSScriptRoot "..\data\mwm.db"),
    [Parameter(Mandatory)][string]$PatientId,
    [string]$ScheduledDate = "today",
    [string]$FamilyKanji = "",
    [string]$GivenKanji = "",
    [string]$FamilyKana = "",
    [string]$GivenKana = "",
    [string]$BirthDate = "",
    [string]$Sex = "",
    [string]$Accession = "",
    [string]$ProcedureId = "",
    [string]$ProcedureDesc = "",
    [string]$ToolsExe = ""
)

$adminArgs = @(
    "admin", "set-current",
    "--profile", $Profile,
    "--profiles-dir", $ProfilesDir,
    "--db", $DbPath,
    "--patient-id", $PatientId,
    "--scheduled-date", $ScheduledDate
)

if ($FamilyKanji -ne "") { $adminArgs += @("--family-kanji", $FamilyKanji) }
if ($GivenKanji -ne "") { $adminArgs += @("--given-kanji", $GivenKanji) }
if ($FamilyKana -ne "") { $adminArgs += @("--family-kana", $FamilyKana) }
if ($GivenKana -ne "") { $adminArgs += @("--given-kana", $GivenKana) }
if ($BirthDate -ne "") { $adminArgs += @("--birth-date", $BirthDate) }
if ($Sex -ne "") { $adminArgs += @("--sex", $Sex) }
if ($Accession -ne "") { $adminArgs += @("--accession", $Accession) }
if ($ProcedureId -ne "") { $adminArgs += @("--procedure-id", $ProcedureId) }
if ($ProcedureDesc -ne "") { $adminArgs += @("--procedure-desc", $ProcedureDesc) }

if ($ToolsExe -ne "") {
    & $ToolsExe @adminArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @adminArgs
}
