<#
.SYNOPSIS
  実装指示書 v2 T10「ホットリロード＋自動ロールバック」用の現地運用スクリプト。
  稼働中の mwm-scp を止めずに、文字コード案①（BMD_HOLOGIC）／②（alt1）／④（alt2）を切り替える。

.DESCRIPTION
  対象プロファイルファイル（既定 config/profiles/BMD_HOLOGIC.jsonc）の "charset" ブロックだけを
  指定した案の内容に置き換えて保存する。mwm-scp は config/profiles/ を 500ms デバウンスで監視しており、
  保存後まもなく自動的に再読込・再検証し、成功すれば次のアソシエーションから新しい文字コードで応答する。
  検証に失敗した場合は稼働中の設定を維持する（自動ロールバック。mwm-scp 側のログを確認すること）。

  9/28 現地接続テストで、初期案（①）が実機と合わない場合の切り替え手順として使う。

.PARAMETER Plan
  1 = 案①（ISO_IR 192 / PN第1群=全角カナ・第2群=漢字・第3群=全角カナ）※既定
  2 = 案②（ISO_IR 192 / PN第1群=空・第2群=漢字・第3群=全角カナ）
  4 = 案④（ISO_IR 13 単独 / 半角カナのみ・漢字を送らない）

.PARAMETER ProfilePath
  切り替え対象のプロファイルファイル。既定は config/profiles/BMD_HOLOGIC.jsonc
  （mwm-scp を --profile BMD_HOLOGIC で起動している前提）。

.EXAMPLE
  .\scripts\switch-charset.ps1 -Plan 4
  # 稼働中の BMD_HOLOGIC を案④（ISO_IR 13 単独）に切り替える。

.EXAMPLE
  .\scripts\switch-charset.ps1 -Plan 1 -ProfilePath C:\ascube-mwm\config\profiles\BMD_HOLOGIC.jsonc
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(1, 2, 4)]
    [int]$Plan,

    [string]$ProfilePath = (Join-Path $PSScriptRoot "..\config\profiles\BMD_HOLOGIC.jsonc")
)

$ErrorActionPreference = "Stop"

$charsetBlocks = @{
    1 = @'
  "charset": {
    "specificCharacterSet": "ISO_IR 192",
    "patientName": {
      "group1": "kanaFull",
      "group2": "kanji",
      "group3": "kanaFull"
    }
  }
'@
    2 = @'
  "charset": {
    "specificCharacterSet": "ISO_IR 192",
    "patientName": {
      "group1": "none",
      "group2": "kanji",
      "group3": "kanaFull"
    }
  }
'@
    4 = @'
  "charset": {
    "specificCharacterSet": "ISO_IR 13",
    "patientName": {
      "group1": "kanaHalf",
      "group2": "none",
      "group3": "none"
    }
  }
'@
}

$planLabel = @{ 1 = "案①（ISO_IR 192 / 全角カナ+漢字+全角カナ）"; 2 = "案②（ISO_IR 192 / 空+漢字+全角カナ）"; 4 = "案④（ISO_IR 13 単独 / 半角カナのみ）" }

if (-not (Test-Path $ProfilePath)) {
    Write-Error "プロファイルファイルが見つかりません: $ProfilePath"
    exit 1
}

$resolvedPath = (Resolve-Path $ProfilePath).Path
$content = Get-Content -Path $resolvedPath -Raw -Encoding UTF8

# "charset": { ... } ブロック全体（次の1階層目のプロパティの直前まで）を置換する。
# _base.jsonc / 各プロファイルとも charset は最上位の1つのオブジェクトなので、
# 対応する閉じ中括弧までを非貪欲マッチで探す（プロファイル中に他の "charset" 文字列が無い前提）。
$pattern = '"charset"\s*:\s*\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}'
if ($content -notmatch $pattern) {
    Write-Error "このファイルには置換対象の \"charset\" ブロックが見つかりませんでした。手動で確認してください: $resolvedPath"
    exit 1
}

# MatchEvaluator を使う（置換文字列に $1 等として解釈されうる文字が無いことを保証するため）。
$newContent = [regex]::Replace($content, $pattern, { param($m) $charsetBlocks[$Plan] }, 1)

Set-Content -Path $resolvedPath -Value $newContent -Encoding UTF8 -NoNewline

Write-Host "OK: $resolvedPath の charset を $($planLabel[$Plan]) に切り替えました。" -ForegroundColor Green
Write-Host "mwm-scp のログで「ProfileHotReloadService: プロファイルの再読込に成功しました」を確認してください。"
Write-Host "失敗した場合は稼働中の設定が維持されます（ログにエラー理由が出ます）。"
Write-Host ""
Write-Host "次のアソシエーションから新しい設定が有効になります。確認コマンド:"
Write-Host "  findscu -v -k `"0008,0052=WORKLIST`" -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 11112"
