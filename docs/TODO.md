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

- [ ] （S-1 が × の場合）.NET 8 から .NET 10 への移行
- [ ] U-5: 年度をまたいで不変の患者ID の確定
- [ ] U-11: PACS の UTF-8 / Enhanced SR 対応確認
- [ ] Calling AE 照合を ignore から strict へ
- [ ] logDataPdus を通常運用では false へ
- [ ] THIRD-PARTY-NOTICES.txt と sbom.json の整備
