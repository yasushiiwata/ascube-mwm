<#
.SYNOPSIS
  C-FIND を実行して、実際に返る受診者情報を確認する。
  既定は装置の想定挙動（Days Back 60 / Forward 2）を再現する。

.PARAMETER PatientId
  PatientID を指名して絞り込みたい場合に指定する。
#>
[CmdletBinding()]
param(
    [string]$MwmHost = "127.0.0.1",
    [int]$Port = 11112,
    [string]$CallingAe = "MARK_FIND",
    [string]$CalledAe = "ASCUBE_MWM",
    [string]$PatientId = "",
    [switch]$EmulateApexDefaults = $true,
    [string]$ToolsExe = ""
)

$scuArgs = @("scu", "find", "--host", $MwmHost, "--port", $Port, "--aet", $CallingAe, "--aec", $CalledAe)
if ($EmulateApexDefaults) {
    $scuArgs += "--emulate-apex-defaults"
}
if ($PatientId -ne "") {
    $scuArgs += @("--patient-id", $PatientId)
}

if ($ToolsExe -ne "") {
    & $ToolsExe @scuArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @scuArgs
}
