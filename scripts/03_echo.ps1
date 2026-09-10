<#
.SYNOPSIS
  疎通確認（C-ECHO）。「反応があるか」だけを最速で確認したいときに使う。
#>
[CmdletBinding()]
param(
    [string]$MwmHost = "127.0.0.1",
    [int]$Port = 11112,
    [string]$CallingAe = "MARK_ECHO",
    [string]$CalledAe = "ASCUBE_MWM",
    [string]$ToolsExe = ""
)

$scuArgs = @("scu", "echo", "--host", $MwmHost, "--port", $Port, "--aet", $CallingAe, "--aec", $CalledAe)

if ($ToolsExe -ne "") {
    & $ToolsExe @scuArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @scuArgs
}
