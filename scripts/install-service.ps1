<#
.SYNOPSIS
  実装指示書 v2 T12：Ascube.Mwm.Scp を Windows サービスとしてインストールする。
  自動起動・異常終了時の自動復帰（sc.exe failure）を設定する。

.DESCRIPTION
  1. dotnet publish で -InstallDir に配置する（フレームワーク依存。.NET 10 ランタイムは
     対象機に別途インストール済みであることが前提）。
  2. 既に -InstallDir が存在する場合（更新インストール）は、実行前に
     "<InstallDir>.backup-<timestamp>" へまるごと退避する（rollback.ps1 が戻す先）。
  3. config/profiles/*.jsonc を -InstallDir\config\profiles にコピーする。
  4. New-Service でサービス登録し、sc.exe failure で異常終了時の自動復帰を設定する。
  5. -DataDir（ワークリストDB・監査DB・captures）にサービスアカウントの読み書き権限を付与する。

  ⚠ 管理者権限のPowerShellで実行すること。システムへの変更（サービス登録・ACL変更）を伴うため、
    このリポジトリの開発セッションでは自動実行しない。実行前に内容を確認すること。

.PARAMETER ServiceAccount
  既定は LocalSystem。専用サービスアカウントを使う場合は事前にアカウントを作成し、
  このパラメータに "ドメインまたはコンピュータ名\アカウント名" の形式で渡すこと
  （パスワードは New-Service の -Credential で別途渡す必要がある。このスクリプトは
  簡略化のため LocalSystem を既定にしている。運用ポリシーに応じて変更すること）。
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "AscubeMwm",
    [string]$DisplayName = "Ascube MWM (DICOM Modality Worklist SCP)",
    [string]$InstallDir = "C:\Ascube\Mwm",
    [string]$DataDir = "C:\Ascube\MwmData",
    [string]$SourceRoot = (Join-Path $PSScriptRoot ".."),
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ServiceAccount = "LocalSystem"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "管理者権限のPowerShellで実行してください。"
    exit 1
}

# --- 1. 既存インストールのバックアップ（rollback.ps1 が戻す先） ---
if (Test-Path $InstallDir) {
    $backupDir = "$InstallDir.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Write-Host "既存の $InstallDir を $backupDir へバックアップします。"
    Copy-Item -Path $InstallDir -Destination $backupDir -Recurse -Force

    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-Host "既存サービス $ServiceName を停止します。"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
}

# --- 2. publish ---
Write-Host "dotnet publish -> $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
dotnet publish (Join-Path $SourceRoot "src\Ascube.Mwm.Scp") -c Release -o $InstallDir --self-contained false
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish に失敗しました。"
    exit 1
}

# --- 3. プロファイルの配置 ---
$profilesOut = Join-Path $InstallDir "config\profiles"
New-Item -ItemType Directory -Force -Path $profilesOut | Out-Null
Copy-Item -Path (Join-Path $SourceRoot "config\profiles\*.jsonc") -Destination $profilesOut -Force

# --- 4. データディレクトリ ---
New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $DataDir "captures") | Out-Null

if ($ServiceAccount -ne "LocalSystem") {
    Write-Host "データディレクトリに $ServiceAccount への読み書き権限を付与します: $DataDir"
    $acl = Get-Acl $DataDir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($ServiceAccount, "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($rule)
    Set-Acl -Path $DataDir -AclObject $acl
}

# --- 5. サービス登録 ---
$exePath = Join-Path $InstallDir "Ascube.Mwm.Scp.exe"
$dbPath = Join-Path $DataDir "mwm.db"
$auditDbPath = Join-Path $DataDir "mwm-audit.db"
$capturesPath = Join-Path $DataDir "captures"
$binaryArgs = "--profile $Profile --profiles-dir `"$profilesOut`" --db `"$dbPath`" --audit-db `"$auditDbPath`" --captures-dir `"$capturesPath`""
$binaryPathName = "`"$exePath`" $binaryArgs"

Write-Host "サービスを登録します: $ServiceName"
New-Service -Name $ServiceName -DisplayName $DisplayName -BinaryPathName $binaryPathName -StartupType Automatic -Description "武田病院健診センター向け DICOM Modality Worklist SCP（BRIDGE-Naviの受診者情報を配信）"

# --- 6. 異常終了時の自動復帰（実装指示書 v2 T12） ---
Write-Host "異常終了時の自動復帰を設定します（5秒→5秒→60秒後にリスタート、24時間でカウンタリセット）。"
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

# --- 7. 起動 ---
Write-Host "サービスを起動します。"
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3
Get-Service -Name $ServiceName | Format-List Name, Status, StartType

Write-Host ""
Write-Host "OK: インストールが完了しました。動作確認: .\scripts\03_echo.ps1" -ForegroundColor Green
Write-Host "ロールバックする場合: .\scripts\rollback.ps1 -ServiceName $ServiceName -InstallDir $InstallDir"
