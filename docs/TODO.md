# TODO / 環境メモ

## 検証時の注意（重要）

⚠ **T0/S-2 の結果、ISO 2022 は不採用になった**（fo-dicomがエスケープシーケンスを出力せず、
規格不適合なバイト列になるため。CLAUDE.md 規則15）。以下は不採用が判明する前、当時の記録として残す。

- S-2（文字コードの往復検証）の合否は16進バイト列で判定する。
  この端末のコンソールは CP932（Shift-JIS）のため、dcmdump の画面表示で
  判断すると誤判定する。判定は次のいずれかで行う。
    - Format-Hex -Path .\test.dcm
    - dcmdump +U8 .\test.dcm
  ISO 2022 の検証では患者氏名にエスケープシーケンスが現れることを16進で確認する。
  **漢字への切替は組合せによらず `1B 24 42`（ESC $ B）。復帰は組合せで値が違う**：
  `ISO 2022 IR 6\ISO 2022 IR 87` の組合せなら ASCII 復帰 `1B 28 42`（ESC ( B）だが、
  `ISO 2022 IR 13\ISO 2022 IR 87` の組合せでは JIS X 0201 Roman への復帰 `1B 28 4A`（ESC ( J）になる。
  ⚠ 以前の記述はこの2つを混同し、後者の組合せでも `1B 28 42` になると誤って書いていた。
- コンソールを UTF-8 にする場合: chcp 65001

## 導入済みツールの版（SBOM 用）

| ツール | 版 |
|---|---|
| Git for Windows | 2.55.0.3 |
| Claude Code | 2.1.258 |
| .NET SDK | 10.0.400 |
| DCMTK | 3.7.0（2025-12-15）wlmscpfs 同梱・builtin-dict |

## 本番リリース前にやること

- [x] U-5: 年度をまたいで不変の患者ID の確定 → **解消。依頼電文 項番3「個人番号」（12桁・年度不変）。**
      レイアウト備考に「個人ユニークキー」と明記。根拠: `docs/依頼電文マッピング表_v1.1.md` §2 項番3。
      PatientID に入れるのは項番3のみで、項番4（受診番号・毎回変わる）と取り違えないこと（CLAUDE.md 規則18）。
- [ ] 自前 PN エンコーダ（ISO 2022 対応）／見積 1〜2日／着手条件：9/28 で UTF-8 が通らなかった場合
- [ ] 実機(APEX 5.6)での文字コード検証（3案とも机上検証のみ。9/28 に実施）
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
  → **T7 では ISO 2022 案（③）を作らない。**`docs/設計変更メモ_v2.1.md` A-2 の判断により、
  用意するのは ①`ISO_IR 192`（PN第1群=全角カナ）／②`ISO_IR 192`（第1群=空）／
  ④`ISO_IR 13` 単独（半角カナのみ）の3案のみ。ISO 2022 対応の自前 `byte[]` ビルダは
  「今すぐ2〜3日」ではなく「9/28 で UTF-8 が通らなかった場合に1〜2日」の事後対応として
  上の「本番リリース前にやること」に残す（pcapで実機の挙動が分かってから作るほうが安い）。
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
