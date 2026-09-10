<#
.SYNOPSIS
  「0件です」の原因を名指しで特定する。実装指示書 v2 T8/T13：
  9/28に「0件です」で止まらないための最重要コマンド。

.PARAMETER RunId
  監査ログのRunId（explain-query --run の値）。省略時は直近のRunIdを自動で使う。
#>
[CmdletBinding()]
param(
    [Nullable[long]]$RunId = $null,
    [string]$AuditDb = (Join-Path $PSScriptRoot "..\data\mwm-audit.db"),
    [string]$ToolsExe = ""
)

function Invoke-MwmAdmin {
    param([string[]]$AdminArgs)

    if ($ToolsExe -ne "") {
        & $ToolsExe @AdminArgs
    }
    else {
        $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
        & dotnet run --project $projectPath -- @AdminArgs
    }
}

if ($null -eq $RunId) {
    Write-Host "RunId 未指定：admin health で直近のRunIdを確認してください（--run <id> を明示指定するのが確実です）。" -ForegroundColor Yellow
    Invoke-MwmAdmin -AdminArgs @("admin", "health", "--profile", "BMD_HOLOGIC", "--audit-db", $AuditDb)
    exit 2
}

Invoke-MwmAdmin -AdminArgs @("admin", "explain-query", "--run", $RunId.ToString(), "--audit-db", $AuditDb)
