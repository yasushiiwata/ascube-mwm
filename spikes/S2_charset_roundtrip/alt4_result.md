# T0 / alt4（ISO_IR 13 単独値）検証結果

実装指示書 §1-3 の受入条件に対する結果。判定は16進バイト列（生バイト直接抽出）と
DCMTK dcmdump（別実装）で行う。コンソール表示（CP932）は判定に使わない。

## 総合判定: ○ 合格

## ISO_IR13_features — ○ PASS
- charset申告: ISO_IR 13
- ファイル: C:\dev\ascube-mwm\spikes\S2_charset_roundtrip\out\ISO_IR13_features.dcm (454 bytes)
- PatientName 生バイト(hex): C0 DE CA DF B0 A5 5E C4 B8 C5 B6 DE
- 1B (エスケープ) 検出: False（期待: False）
- 0xA1〜0xDF 範囲外(区切り/パディング除く)のバイト数: 0
- fo-dicom自己再読込: 一致
- dcmdump 警告/エラー出力: (なし)
- dcmdump出力行: (0010,0010) PN [ﾀﾞﾊﾟｰ･^ﾄｸﾅｶﾞ]     #  34, 1 PatientName
- dcmdump値と元の文字列: 一致


## ISO_IR13_stress30 — ○ PASS
- charset申告: ISO_IR 13
- ファイル: C:\dev\ascube-mwm\spikes\S2_charset_roundtrip\out\ISO_IR13_stress30.dcm (472 bytes)
- PatientName 生バイト(hex): B1 B2 B3 B4 B5 B1 B2 B3 B4 B5 B1 B2 B3 B4 B5 B1 B2 B3 B4 B5 B1 B2 B3 B4 B5 B1 B2 B3 B4 B5
- 1B (エスケープ) 検出: False（期待: False）
- 0xA1〜0xDF 範囲外(区切り/パディング除く)のバイト数: 0
- fo-dicom自己再読込: 一致
- dcmdump 警告/エラー出力: (なし)
- dcmdump出力行: (0010,0010) PN [ｱｲｳｴｵｱｲｳｴｵｱｲｳｴｵｱｲｳｴｵｱｲｳｴｵｱｲｳｴｵ] #  90, 1 PatientName
- dcmdump値と元の文字列: 一致


## ISO_IR13_kanji_mixed — ○ PASS
- charset申告: ISO_IR 13
- 元のPatientName: 山田^ﾀﾛｳ（ISO_IR 13 では原理的に表現できないはず）
- PatientName 生バイト(hex): 8E 52 93 63 5E C0 DB B3
- fo-dicom自己再読込値: 山田^ﾀﾛｳ（hex: 5C71 7530 005E FF80 FF9B FF73）
- 元の文字列と自己再読込値が一致するか: 一致（要注意）
- dcmdump exit=1
- dcmdump stderr: W: DcmItem: An error occurred during the conversion to UTF-8 encoding, the value of SpecificCharacterSet (0008,0005) is not updated
E: dcmdump: Cannot convert character encoding: Illegal byte sequence: converting file to UTF-8: C:\dev\ascube-mwm\spikes\S2_charset_roundtrip\out\ISO_IR13_kanji_mixed.dcm
- dcmdump出力行: (なし)
- dcmdump値と元の文字列が一致するか: 不一致（検出できている）
- 総合判定: 不一致/エラーとして検出できた（Suppressed化の材料になる）


