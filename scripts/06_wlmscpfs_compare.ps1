<#
.SYNOPSIS
  T13：DCMTK 純正の参照実装 wlmscpfs と mwm-scp の応答を「意味比較」する（タグ・VR・値・SQ構造。
  可変値＝UID・時刻・MessageID は比較から除外）。実装指示書 v2 T13「wlmscpfs との意味比較」に対応。

.DESCRIPTION
  1. 参照データ（患者名・患者ID等）から DCMTK 純正の .wl ファイルを dump2dcm で生成し、
     AE Title 名のサブディレクトリ配下に置く（wlmscpfs は Called AE Title をディレクトリ名として
     解釈する。ドキュメントに明記が無い挙動なので注意）。
  2. wlmscpfs をそのディレクトリの親を -dfp として起動する。
  3. 同じ返却キー一覧（query.dcm）で mwm-scp と wlmscpfs の両方に findscu を投げ、応答を
     dcmdump でテキスト化する。
  4. 両方の出力から UID・時刻・(0002,0003) 等の可変値を含む行をマスクしてから diff する。

.PARAMETER MwmHost
  比較対象の mwm-scp のホスト（既定 127.0.0.1）。

.PARAMETER MwmPort
  比較対象の mwm-scp の待受ポート（既定 11112。BMD_HOLOGIC の network.port と合わせること）。

.PARAMETER MwmAe
  比較対象の mwm-scp の Called AE Title（既定 ASCUBE_MWM）。

.PARAMETER WlmPort
  wlmscpfs（参照実装）を起動するポート（既定 11113。mwm-scp と衝突しないこと）。

.PARAMETER DcmtkBinDir
  findscu / dcmdump / dump2dcm / wlmscpfs の場所。省略時は PATH 上のものを使う
  （この開発機では chocolatey の C:\ProgramData\chocolatey\bin\ に入っている）。

.EXAMPLE
  # 別ターミナルで mwm-scp を起動済みであること：
  #   dotnet run --project src/Ascube.Mwm.Tools -- admin validate --profile BMD_HOLOGIC
  #   dotnet run --project src/Ascube.Mwm.Scp -- --console --profile BMD_HOLOGIC
  # （BRIDGE-Navi 側で受診者を1人 SetCurrentAsync しておくこと。無ければ0件同士の比較になる。）
  .\scripts\06_wlmscpfs_compare.ps1

.NOTES
  wlmscpfs は「返却キーとして要求されなかった属性は返さない」標準準拠の挙動をする
  （mwm-scp は現行実装ではプロファイル定義の要素を常に全部返す。差分比較はこの前提を踏まえて読むこと。
  意味比較で本質的に重要なのは「両者とも要求したタグを正しい VR・値で返せているか」であり、
  mwm-scp が追加で返す余剰タグ自体は規格違反ではない）。
#>
[CmdletBinding()]
param(
    [string]$MwmHost = "127.0.0.1",
    [int]$MwmPort = 11112,
    [string]$MwmAe = "ASCUBE_MWM",
    [int]$WlmPort = 11113,
    [string]$DcmtkBinDir = "",

    # 参照データ（mwm-scp 側は事前に BRIDGE-Navi 経由で同じ内容を SetCurrentAsync しておくこと）。
    [string]$RefPatientName = "Yamada^Taro",
    [string]$RefPatientId = "000012345678",
    [string]$RefBirthDate = "19800101",
    [string]$RefSex = "M",
    [string]$RefStudyInstanceUid = "2.25.999999999999999999999999999999999999",
    [string]$RefAccessionNumber = "2609280001010053",
    [string]$RefProcedureId = "00110",
    [string]$RefProcedureDesc = "WLM COMPARE TEST",
    [string]$RefScheduledDate = "20260928",
    [string]$RefScheduledTime = "090000"
)

function Resolve-DcmtkExe {
    param([string]$Name)
    if ($DcmtkBinDir -ne "") {
        $p = Join-Path $DcmtkBinDir "$Name.exe"
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command "$Name.exe" -ErrorAction SilentlyContinue
    if ($null -ne $cmd) { return $cmd.Source }
    throw "$Name.exe が見つからない（PATH か -DcmtkBinDir を確認すること）。"
}

$findscu = Resolve-DcmtkExe "findscu"
$dcmdump = Resolve-DcmtkExe "dcmdump"
$dump2dcm = Resolve-DcmtkExe "dump2dcm"
$wlmscpfs = Resolve-DcmtkExe "wlmscpfs"

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ascube-mwm-wlm-compare-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$wlmAeDir = Join-Path $work "WLM_REF"
New-Item -ItemType Directory -Path $wlmAeDir -Force | Out-Null
# wlmscpfs 固有の挙動：AE Title 名ディレクトリ内に空の lockfile が無いと
# "SetReadlock" エラーで起動時に読めない（ドキュメント未記載。DCMTKフォーラムで確認済み）。
New-Item -ItemType File -Path (Join-Path $wlmAeDir "lockfile") -Force | Out-Null

# --- 参照 .wl ファイルを生成 ---
# 完全性チェック（-efr 既定有効）に通るには Type1 相当（StepStartTime・StepID 等）に実値が要る。
$refDump = @"
(0008,0005) CS [ISO_IR 192]
(0010,0010) PN [$RefPatientName]
(0010,0020) LO [$RefPatientId]
(0010,0030) DA [$RefBirthDate]
(0010,0040) CS [$RefSex]
(0020,000d) UI [$RefStudyInstanceUid]
(0008,0050) SH [$RefAccessionNumber]
(0040,1001) SH [$RefProcedureId]
(0032,1060) LO [$RefProcedureDesc]
(0040,0100) SQ
(fffe,e000) na
(0008,0060) CS [MW]
(0040,0001) AE [$MwmAe]
(0040,0002) DA [$RefScheduledDate]
(0040,0003) TM [$RefScheduledTime]
(0040,0007) LO [$RefProcedureDesc]
(0040,0009) SH [$RefProcedureId]
(fffe,e00d) na
(fffe,e0dd) na
"@
$refDumpPath = Join-Path $work "reference.dump"
Set-Content -Path $refDumpPath -Value $refDump -Encoding ASCII
& $dump2dcm $refDumpPath (Join-Path $wlmAeDir "reference.wl") 2>&1 | Out-Null

# --- 両サーバに同じ返却キーを要求する query.dcm を生成 ---
$queryDump = @"
(0008,0052) CS [WORKLIST]
(0010,0010) PN []
(0010,0020) LO []
(0010,0030) DA []
(0010,0040) CS []
(0020,000d) UI []
(0008,0050) SH []
(0040,1001) SH []
(0032,1060) LO []
(0040,0100) SQ
(fffe,e000) na
(0008,0060) CS []
(0040,0001) AE []
(0040,0002) DA []
(0040,0003) TM []
(0040,0007) LO []
(fffe,e00d) na
(fffe,e0dd) na
"@
$queryDumpPath = Join-Path $work "query.dump"
Set-Content -Path $queryDumpPath -Value $queryDump -Encoding ASCII
$queryDcmPath = Join-Path $work "query.dcm"
& $dump2dcm $queryDumpPath $queryDcmPath 2>&1 | Out-Null

# --- wlmscpfs（参照実装）を起動 ---
$wlmLog = Join-Path $work "wlmscpfs.log"
$wlmProcess = Start-Process -FilePath $wlmscpfs -ArgumentList @("-dfp", $work, "-s", "$WlmPort") `
    -RedirectStandardOutput $wlmLog -RedirectStandardError "$wlmLog.err" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1

try {
    $wlmOutDir = Join-Path $work "wlm_out"
    New-Item -ItemType Directory -Path $wlmOutDir -Force | Out-Null
    & $findscu -aet CMP_SCU -aec WLM_REF -W -xe -X -od $wlmOutDir 127.0.0.1 $WlmPort $queryDcmPath 2>&1 | Out-Null

    $mwmOutDir = Join-Path $work "mwm_out"
    New-Item -ItemType Directory -Path $mwmOutDir -Force | Out-Null
    & $findscu -aet CMP_SCU -aec $MwmAe -W -xe -X -od $mwmOutDir $MwmHost $MwmPort $queryDcmPath 2>&1 | Out-Null

    $wlmRsp = Join-Path $wlmOutDir "rsp0001.dcm"
    $mwmRsp = Join-Path $mwmOutDir "rsp0001.dcm"

    if (-not (Test-Path $wlmRsp)) {
        Write-Warning "wlmscpfs（参照実装）から応答が無かった。$wlmLog / $wlmLog.err を確認すること。"
    }
    if (-not (Test-Path $mwmRsp)) {
        Write-Warning "mwm-scp（$MwmHost`:$MwmPort）から応答が無かった。現在の受診者が設定されているか確認すること（1人モデル：CurrentEntry が無ければ0件+Successが正常）。"
    }

    function Get-NormalizedDump {
        param([string]$DcmPath)
        if (-not (Test-Path $DcmPath)) { return @() }
        $lines = & $dcmdump $DcmPath 2>&1
        # 可変値（UID・時刻・MessageID・実装依存メタ情報）を含む行をマスクしてから比較する。
        $variableTagPattern = "0002,0003|0002,0013|StudyInstanceUID|ScheduledProcedureStepStartTime|AccessionNumber"
        $result = @($lines | Where-Object { $_ -notmatch "^#" -and $_.Trim() -ne "" } | ForEach-Object {
            if ($_ -match $variableTagPattern) {
                # タグ・VR・属性名は残し、値部分だけ ### に置換する。
                $_ -replace "\[[^\]]*\]", "[###]"
            } else {
                $_
            }
        })
        # PowerShell はパイプライン結果が0件/1件だと配列を暗黙にアンラップすることがあるため、
        # Compare-Object に渡す前に必ず配列型へ固定する。
        return , $result
    }

    $wlmNormalized = @(Get-NormalizedDump -DcmPath $wlmRsp)
    $mwmNormalized = @(Get-NormalizedDump -DcmPath $mwmRsp)

    Write-Host "=== wlmscpfs（参照実装, port $WlmPort）応答 ===" -ForegroundColor Cyan
    $wlmNormalized | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "=== mwm-scp（$MwmHost`:$MwmPort）応答 ===" -ForegroundColor Cyan
    $mwmNormalized | ForEach-Object { Write-Host "  $_" }
    Write-Host ""

    $diff = Compare-Object -ReferenceObject $wlmNormalized -DifferenceObject $mwmNormalized
    if ($null -eq $diff -or @($diff).Count -eq 0) {
        Write-Host "一致：タグ・VR・値・SQ構造に意味的な差分なし（可変値は除外済み）。" -ForegroundColor Green
    } else {
        Write-Host "差分あり（<= が wlmscpfs のみ、=> が mwm-scp のみ）：" -ForegroundColor Yellow
        $diff | ForEach-Object {
            $mark = if ($_.SideIndicator -eq "<=") { "<=" } else { "=>" }
            Write-Host "  $mark $($_.InputObject)"
        }
    }
}
finally {
    if ($null -ne $wlmProcess -and -not $wlmProcess.HasExited) {
        Stop-Process -Id $wlmProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "作業ファイル: $work" -ForegroundColor DarkGray
