# TODO / 環境メモ

## 検証時の注意（重要）

- S-2（文字コードの往復検証）の合否は16進バイト列で判定する。
  この端末のコンソールは CP932（Shift-JIS）のため、dcmdump の画面表示で
  判断すると誤判定する。判定は次のいずれかで行う。
    - Format-Hex -Path .\test.dcm
    - dcmdump +U8 .\test.dcm
  ISO 2022 の検証では患者氏名に 1B 24 42（漢字切替）と 1B 28 42（ASCII復帰）が
  現れることを16進で確認する。
- コンソールを UTF-8 にする場合: chcp 65001

## 導入済みツールの版（SBOM 用）

| ツール | 版 |
|---|---|
| Git for Windows | 2.55.0.3 |
| Claude Code | 2.1.258 |
| .NET SDK | 10.0.4xx（実際の値に更新すること） |
| DCMTK | 3.7.0（2025-12-15）wlmscpfs 同梱・builtin-dict |

## 本番リリース前にやること

- [x] （S-1 が × の場合）.NET 8 から .NET 10 への移行 → S-1 は○合格。net10.0 のまま継続。
- [ ] U-5: 年度をまたいで不変の患者ID の確定
- [ ] U-11: PACS の UTF-8 / Enhanced SR 対応確認
- [ ] Calling AE 照合を ignore から strict へ
- [ ] logDataPdus を通常運用では false へ
- [ ] THIRD-PARTY-NOTICES.txt と sbom.json の整備

## T0（2026-09-05実施）で確定した設計上の決定事項

- **T7は「fo-dicom標準のPN書き込みを使わない」前提で計画する。**
  fo-dicom 5.2.6は `SpecificCharacterSet = "ISO 2022 IR 13\ISO 2022 IR 87"` を指定しても
  コード拡張のエスケープシーケンス（漢字切替 `1B 24 42`・半角カナ切替 `1B 29 49`・ASCII復帰 `1B 28 42`）
  を一切出力せず、実際には CS 932 (Shift_JIS) の生バイトを書き込みながらヘッダだけ ISO 2022 と
  名乗る非準拠ファイルを作る。DCMTK dcmdump はこれを "Illegal byte sequence" として読み込み拒否する。
  → PN の符号拡張バイト列は自前の `byte[]` ビルダで作る（実装指示書 T0-S-2 の迂回策どおり）。
  → **2〜3日のバッファを T7 に確保する。** 開発計画 v2 §6-1「作らない」リストの拡大で吸収する。
  根拠: `spikes/S2_charset_roundtrip/`（実行結果は `out/report.md` / `out/report_no-register.md`。
  `out/` は `.gitignore` 済みなので再実行して確認すること: `dotnet run --project spikes/S2_charset_roundtrip`）。

- **`ISO_IR 192`（UTF-8）は fo-dicom 標準の書き込みで問題なし。**
  漢字・JIS X0208外の外字（髙 U+9AD9・﨑 U+FA11・德 U+5FB7）・濁点・長音・30文字級すべて
  dcmdump（独立実装）で完全一致。ただし外字がJIS X0208(IR87)側では表現できない点は
  BMD_HOLOGIC以外のIR13\IR87系プロファイルで踏む可能性があるので、T7の`required`/`onMissing`設計時に
  「変換不能文字＝代替文字で誤魔化さずSuppressed」という CLAUDE.md 絶対規則4の運用を PN にも徹底すること。

- **T9（raw stream recorder）は迂回策②を採用する。**
  `DicomServiceOptions.LogDataPDUs`/`LogDimseDatasets` は構造化済みログ（タグ/VR/値のダンプ）であって
  生バイトの16進ダンプではないため、生バイト復元・pcap突合には使えない（迂回策①は不採用）。
  代わりに DI 経由で `INetworkManager` を差し替える（`AddFellowOakDicom()` の**後**に
  `services.AddNetworkManager<T>()` を呼ぶ。`TryAddNetworkManager` は `AddFellowOakDicom()` が
  先に既定実装を登録済みのため効かない）。差し替えた `INetworkManager.CreateNetworkStream(TcpClient,...)`
  が返す `INetworkStream.AsStream()` を tee する `Stream` デコレータで、bounded channel 経由・
  fo-dicom 無改変のまま送受信バイトを100%捕捉できることを確認した。
  根拠・実装サンプル: `spikes/S3_raw_recorder/Program.cs`（`RecordingNetworkManager`/`RecordingNetworkStream`/
  `TeeStream`/`RawRecorder`）、結果は `out/S3_conclusion.md`。
