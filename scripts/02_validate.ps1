<#
.SYNOPSIS
  現在の設定ファイルを検証する（起動前チェック・トラブル時の切り分け用）。
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$ToolsExe = ""
)

$adminArgs = @("admin", "validate", "--profile", $Profile, "--profiles-dir", $ProfilesDir)

if ($ToolsExe -ne "") {
    & $ToolsExe @adminArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @adminArgs
}
