<#
.SYNOPSIS
  実装指示書 v2 T12：現地テストの証跡一式（プロファイル・監査ログ・T9生キャプチャ・
  dumpcapのpcap・ログ）を1つのフォルダにまとめる。admin export-support-bundle（T11）の
  zipに、T9の生キャプチャファイルとWiresharkのpcapngも加えたもの（こちらはサイズが
  大きくなりうるので、必要なときに手動で実行する運用とする）。

.PARAMETER OutDir
  収集先ディレクトリ。既定は evidence\<タイムスタンプ>\（.gitignore 済み）。
#>
[CmdletBinding()]
param(
    [string]$Profile = "BMD_HOLOGIC",
    [string]$ProfilesDir = (Join-Path $PSScriptRoot "..\config\profiles"),
    [string]$AuditDb = (Join-Path $PSScriptRoot "..\data\mwm-audit.db"),
    [string]$CapturesDir = (Join-Path $PSScriptRoot "..\captures"),
    [string]$OutDir = "",
    [string]$ToolsExe = ""
)

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
if ($OutDir -eq "") {
    $OutDir = Join-Path $PSScriptRoot "..\evidence\$stamp"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Write-Host "証跡収集先: $OutDir"

# 1. 証跡パッケージ（プロファイル・直近監査ログ）
$bundlePath = Join-Path $OutDir "support-bundle.zip"
$exportArgs = @(
    "admin", "export-support-bundle",
    "--profile", $Profile,
    "--profiles-dir", $ProfilesDir,
    "--audit-db", $AuditDb,
    "--captures-dir", $CapturesDir,
    "--out", $bundlePath
)

if ($ToolsExe -ne "") {
    & $ToolsExe @exportArgs
}
else {
    $projectPath = Join-Path $PSScriptRoot "..\src\Ascube.Mwm.Tools"
    & dotnet run --project $projectPath -- @exportArgs
}

# 2. T9の生キャプチャ（.bin / .index.jsonl / .sha256）をまるごとコピー
if (Test-Path $CapturesDir) {
    $rawOut = Join-Path $OutDir "raw-captures"
    New-Item -ItemType Directory -Force -Path $rawOut | Out-Null
    Copy-Item -Path (Join-Path $CapturesDir "*") -Destination $rawOut -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "T9生キャプチャをコピーしました: $rawOut"
}

# 3. dumpcap の pcapng（あれば）
$pcapDir = Join-Path $CapturesDir "pcap"
if (Test-Path $pcapDir) {
    $pcapOut = Join-Path $OutDir "pcap"
    New-Item -ItemType Directory -Force -Path $pcapOut | Out-Null
    Copy-Item -Path (Join-Path $pcapDir "*") -Destination $pcapOut -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "pcapをコピーしました: $pcapOut"
}

Write-Host ""
Write-Host "OK: 証跡一式を $OutDir にまとめました。" -ForegroundColor Green
