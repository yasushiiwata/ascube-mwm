<#
.SYNOPSIS
  ワークリストを空にする（mwm-admin admin clear-current の薄いラッパー）。07_set-current.ps1 の対。
  以後の C-FIND は 0件+Success になる。
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$DbPath = (Join-Path $PSScriptRoot "..\data\mwm.db"),
    [string]$ToolsExe = ""
)

$adminArgs = @("admin", "clear-current", "--profile", $Profile, "--profiles-dir", $ProfilesDir, "--db", $DbPath)

if ($ToolsExe -ne "") {
    & $ToolsExe @adminArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @adminArgs
}
