<#
.SYNOPSIS
  実装指示書 v2 T12：install-service.ps1 でのインストール前の状態へ戻す。
  受入条件：5分以内に設置前状態へ戻せること。

.DESCRIPTION
  1. サービスを停止・削除する。
  2. install-service.ps1 が作った "<InstallDir>.backup-<timestamp>" のうち最新のものを
     -InstallDir に復元する（バックアップが無ければ -InstallDir を削除するだけ＝新規インストール前に戻す）。
  3. バックアップに含まれていたサービス定義があれば、同じ内容で再登録して起動する。
     （install-service.ps1 は publish 後のフォルダをまるごとバックアップするだけで、
       サービス定義は保存していないため、直前のインストールと同じパラメータで
       再度 install-service.ps1 を呼ぶ運用を推奨する。バックアップが1回分より前のものが
       必要な場合は -BackupDir で明示的に指定すること。）

  ⚠ 管理者権限のPowerShellで実行すること。
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "AscubeMwm",
    [string]$InstallDir = "C:\Ascube\Mwm",
    [string]$BackupDir = ""
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "管理者権限のPowerShellで実行してください。"
    exit 1
}

# --- 1. サービス停止・削除 ---
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "サービス $ServiceName を停止します。"
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}
else {
    Write-Host "サービス $ServiceName は見つかりませんでした（既に削除済み、または未インストール）。"
}

# --- 2. バックアップの復元 ---
if ($BackupDir -eq "") {
    $candidates = Get-ChildItem -Path (Split-Path $InstallDir -Parent) -Directory -Filter "$(Split-Path $InstallDir -Leaf).backup-*" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending
    if ($candidates.Count -gt 0) {
        $BackupDir = $candidates[0].FullName
    }
}

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($BackupDir -ne "" -and (Test-Path $BackupDir)) {
    Write-Host "バックアップを復元します: $BackupDir -> $InstallDir"
    Copy-Item -Path $BackupDir -Destination $InstallDir -Recurse -Force

    Write-Host ""
    Write-Host "バックアップの復元が完了しました。直前のインストール条件と同じ引数で" -ForegroundColor Yellow
    Write-Host "install-service.ps1 を再実行してサービスを再登録・起動してください" -ForegroundColor Yellow
    Write-Host "（例: .\scripts\install-service.ps1 -ServiceName $ServiceName -InstallDir $InstallDir -Profile <直前のプロファイルID>）" -ForegroundColor Yellow
}
else {
    Write-Host "復元対象のバックアップが見つかりませんでした。$InstallDir を削除しただけの状態（＝新規インストール前）です。"
}

$sw.Stop()
Write-Host ""
Write-Host ("OK: ロールバックが完了しました（所要時間: {0:N0}秒）。" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
if ($sw.Elapsed.TotalMinutes -gt 5) {
    Write-Warning "5分を超えました。受入条件（5分以内）を満たしていません。原因を確認してください。"
}
