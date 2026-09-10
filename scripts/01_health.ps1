<#
.SYNOPSIS
  1回だけ health を表示する（00_watch.ps1 の常時監視と違い、単発確認用）。
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$DbPath = (Join-Path $PSScriptRoot "..\data\mwm.db"),
    [string]$AuditDb = (Join-Path $PSScriptRoot "..\data\mwm-audit.db"),
    [string]$ToolsExe = ""
)

$adminArgs = @("admin", "health", "--profile", $Profile, "--profiles-dir", $ProfilesDir, "--db", $DbPath, "--audit-db", $AuditDb)

if ($ToolsExe -ne "") {
    & $ToolsExe @adminArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @adminArgs
}
