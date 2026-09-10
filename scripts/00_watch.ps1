<#
.SYNOPSIS
  9/28現地接続テストで常時表示しておく監視画面（5秒間隔）。
  実装指示書 v2 T12：「現在の受診者：あり／なし、TTL残 mm:ss」を画面の一番目立つ位置に出す
  （何か起きたときに真っ先に確認する項目のため）。

.PARAMETER Profile
  対象プロファイルID（既定 BMD_HOLOGIC）。

.PARAMETER ProfilesDir
  既定 config/profiles。

.PARAMETER DbPath
  ワークリストDBのパス（既定 data/mwm.db）。

.PARAMETER AuditDb
  監査DBのパス（既定 data/mwm-audit.db）。

.PARAMETER ToolsExe
  mwm-admin の実行ファイル。既定は開発時の "dotnet run --project src/Ascube.Mwm.Tools --"。
  配布物（publish 済み exe）がある場合はそのパスを渡すこと。

.EXAMPLE
  .\scripts\00_watch.ps1
  .\scripts\00_watch.ps1 -Profile BMD_HOLOGIC -IntervalSeconds 5
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$DbPath = (Join-Path $PSScriptRoot "..\data\mwm.db"),
    [string]$AuditDb = (Join-Path $PSScriptRoot "..\data\mwm-audit.db"),
    [string]$ToolsExe = "",
    [int]$IntervalSeconds = 5
)

# mwm-admin（.NET）は既定でUTF-8を標準出力に書く。Windows PowerShell 5.1 は既定でANSI/OEM
# コードページとして読み取ってしまい、日本語（"現在の受診者" のあり/なし判定に使う文字列）が
# 文字化けして正規表現が一致しなくなる。ここで明示的にUTF-8として読み取るよう設定する。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

function Invoke-MwmAdmin {
    param([string[]]$AdminArgs)

    if ($ToolsExe -ne "") {
        & $ToolsExe @AdminArgs
        return
    }

    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @AdminArgs 2>&1
}

while ($true) {
    Clear-Host
    Write-Host "=================================================================" -ForegroundColor DarkGray
    Write-Host "  ascube-mwm 監視画面   $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
    Write-Host "=================================================================" -ForegroundColor DarkGray
    Write-Host ""

    $healthArgs = @("admin", "health", "--profile", $Profile, "--profiles-dir", $ProfilesDir, "--db", $DbPath, "--audit-db", $AuditDb)
    $output = Invoke-MwmAdmin -AdminArgs $healthArgs

    # $output の各行を文字列配列として確実に受け取り、-match の $Matches が正しく効くよう
    # 単一の文字列に結合しておく（配列のまま -match すると $Matches が期待どおり埋まらない）。
    $currentLine = ($output | Where-Object { $_ -match "現在の受診者" }) -join " "
    Write-Host ""
    if ($currentLine -match "あり.*TTL残 (\d\d:\d\d)") {
        Write-Host "  ●●● 現在の受診者：あり（TTL残 $($Matches[1])） ●●●" -ForegroundColor Green -BackgroundColor Black
    }
    elseif ($currentLine -match "TTL切れ") {
        Write-Host "  ▲▲▲ 現在の受診者：あり（TTL切れ！次のC-FINDは0件） ▲▲▲" -ForegroundColor Yellow -BackgroundColor Black
    }
    else {
        Write-Host "  ○○○ 現在の受診者：なし ○○○" -ForegroundColor DarkYellow
    }
    Write-Host ""
    Write-Host "-----------------------------------------------------------------" -ForegroundColor DarkGray

    $output | ForEach-Object { Write-Host "  $_" }

    Write-Host ""
    Write-Host "($IntervalSeconds 秒ごとに自動更新。Ctrl+C で終了)" -ForegroundColor DarkGray

    Start-Sleep -Seconds $IntervalSeconds
}
