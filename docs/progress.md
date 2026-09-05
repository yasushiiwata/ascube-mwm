# 進捗

| 日付 | タスク | 受入条件 | 残課題 |
|---|---|---|---|
| 2026-09-04 | 環境構築 | Git 2.55 / .NET 10 SDK / Claude Code 2.1.258 / DCMTK 3.7.0 | — |
| 2026-09-05 | T0-S1（.NET10+fo-dicom最小SCP） | ○ 合格。echoscu / findscu（DCMTK・独立実装）とも Success。TFM は net10.0 のまま継続。 | なし |
| 2026-09-05 | T0-S2（文字コード往復検証） | △ 部分合格。`ISO_IR 192`（UTF-8）は漢字・外字(髙﨑德)・濁点・長音・30文字級すべて dcmdump（独立実装）で完全一致。`ISO 2022 IR 13\ISO 2022 IR 87` は **× 不合格**：fo-dicomの既定PN書き込みはコード拡張エスケープ(1B 24 42 / 1B 29 49)を一切出力せず、実体は生の CP932(Shift_JIS) バイト列を charset ラベルだけ ISO2022 と偽って書き出していた。DCMTK dcmdump は "Illegal byte sequence" で読み込み自体を拒否。 | **T7で自前 byte[] PN エンコーダに切替が確定。2〜3日を確保すること。** 詳細は `spikes/S2_charset_roundtrip/out/report*.md`。 |
| 2026-09-05 | T0-S3（生バイト記録） | ○ 合格（②を採用）。①`DicomServiceOptions.LogDataPDUs/LogDimseDatasets`は構造化ログのみで生バイト復元不可のため不採用。②DI経由で`INetworkManager`を差し替え（`AddFellowOakDicom()`の**後**に`AddNetworkManager<T>()`）、`INetworkStream.AsStream()`をteeする`Stream`デコレータで送受信バイトをbounded channel経由で完全capture。fo-dicom本体は無改変。③(透過TCPプロキシ)は不要と判断。 | T9実装時に本スパイクの`RawRecorder`/`RecordingNetworkManager`パターンを流用する。詳細は`spikes/S3_raw_recorder/out/S3_conclusion.md`。 |
