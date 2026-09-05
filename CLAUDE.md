# ascube-mwm — DICOM Modality Worklist SCP

武田病院健診センター向け。BRIDGE-Navi の受診者情報を DICOM MWM で検査装置に配信する。
第1弾の対象装置は Hologic APEX 5.6（骨塩定量装置）。現地接続テストは **2026-09-28**。

設計判断の根拠は `docs/開発計画_v2.md`（差分は `docs/設計変更メモ_v2.1.md`）、実装タスクと受入条件は `docs/実装指示書.md` にある。
**本ファイルと矛盾したら `docs/設計変更メモ_v2.1.md` が正。**

---

## 絶対に守る規則

これらは「うっかり破ると本番稼働後まで発覚しない」種類のものです。実装時とレビュー時に必ず確認すること。

1. **StudyInstanceUID は WorkItem の upsert 時に1回だけ採番する。**
   C-FIND のたびに採番してはならない。同じ受診者に問い合わせのたび別 UID が振られ、
   PACS 上で1検査が複数スタディに分裂する。`UidAllocation` の UNIQUE 制約で担保する。
   形式は `2.25.` + UUID の10進表現（DICOM PS3.5 Annex B.2）。
   ⚠ Implementation Class UID には `2.25.` を使わない。

2. **C-FIND の該当0件は `DicomStatus.Success` かつ結果なし。**
   Error を返すと装置画面にエラーが出る。

3. **応答送信中にクライアントが一方的に切断するのは正常系。**
   `OnConnectionClosed` で例外を再スローしない。再スローするとリスナが道連れになり
   「二度と繋がらない」状態になる。FIN と RST の両方で検証すること。

4. **患者に属する値（PatientID / 氏名 / 生年月日 / 性別）を捏造しない。**
   欠損・重複・文字変換損失があればその行を返さず、監査に重大ログを残す。
   固定値で補完してよいのは装置・施設・検査の属性だけ。

5. **SCP は SQLite を読み取り専用で開く。** 書き込む経路を作らない。

6. **`SpecificCharacterSet (0008,0005)` は DicomDataset に最初に設定する。**
   これより後に文字列要素を足さないと符号化が意図どおりにならない実装がある。

7. **`SpecificCharacterSet` に `ISO 2022 IR 87` を単独値で設定しない。**
   多バイト集合は符号拡張（複数値）でのみ使える。
   正しくは `ISO 2022 IR 6\ISO 2022 IR 87`（第1値を空にして `\ISO 2022 IR 87` も可）。

8. **設定ファイルの検証に失敗したら新設定を適用せず、稼働中の設定を維持する。**
   現場での1文字のミスが検査停止に直結してはならない。

9. **Presentation Context は「未サポートのものだけ reject」し、アソシエーション自体は成立させる。**
   Transfer Syntax は Implicit VR LE / Explicit VR LE / Explicit VR BE の3種を accept する。

10. **AE 照合を strict にする場合でも、黙って切らずに A-ASSOCIATE-RJ を正しい理由コードで返す。**
    黙って切ると装置側でタイムアウト（既定20分）まで待たされ、原因も分からない。

11. **fo-dicom を fork・改変しない。** NuGet 参照のまま使う。
    拡張点で足りない場合は `docs/実装指示書.md` T9 の代替路を採る。

12. **GPL コード（Orthanc 等）をリポジトリに入れない。**
    DCMTK / Wireshark は別プロセスで実行する外部ツールであり、同梱もリンクもしない。
    Npcap は OSS ではないのでインストーラに同梱しない。

13. **依存パッケージは版を固定する**（`Directory.Packages.props` で一元管理）。
    リリースごとに `docs/sbom.json` と `docs/THIRD-PARTY-NOTICES.txt` を更新する。

14. **受診者データと通信記録をコミットしない。**
    `*.db` `*.pcapng` `*.dcm` `*.wl` は `.gitignore` 済み。テストデータは
    `tests/fixtures/*.json`（テキスト）で持ち、DB は毎回スクリプトで生成する。

15. **fo-dicom は ISO 2022 のエスケープシーケンスを出力しない（T0/S-2 で確認）。**
    `SpecificCharacterSet` に `ISO 2022 IR 87` / `ISO 2022 IR 13` を含む値を設定しても
    正しいバイト列にならない。**ISO 2022 系のプロファイルを作らない・配らない。**
    使えるのは `ISO_IR 192`（UTF-8）と `ISO_IR 13`（半角カナ単独）だけ。
    ⚠ 規則7（`ISO 2022 IR 87` を単独値にしない）は規格上の話として引き続き有効だが、
    そもそも ISO 2022 系を使わないので実務上は発生しない。

16. **ワークリストに載るのは常に当日の1人だけ（1人モデル）。**
    MWM サーバは BRIDGE-Navi の端末と1対1で対応する。
    **「該当者が特定できないときは0件を返す。古い1人を返さない。」**
    前の受診者を消し忘れて次の人の検査に載せるのが、この構成で最悪の事故。
    ⚠ `fallbackToAllTodayWhenNoActive` のような「とりあえず今日の一覧を返す」機能を作らない。

17. **当日の身長・体重は、当日測定した値だけを送る。**
    依頼電文には前回・前々回の値しか入っていない。
    未測定ならタグごと省略する（`onMissing: omit`）。前回値を今日の値として送らない。
    ⚠ ブースの巡回順は誘導アルゴリズムが決めるため、骨密度が身長体重より
    先になる場合がある。「未測定なら省略」は必ず通る経路。

18. **依頼電文の項番3（個人番号・12桁）と項番4（受診番号・5桁）を取り違えない。**
    PatientID に入れるのは**項番3**。項番4 は毎回変わるので、入れると同じ人が
    毎年別人として登録され、骨密度の経年比較が壊れる。**1年後まで発覚しない。**

---

## 技術スタック

- **C# / .NET 10 LTS**（T0/S-1 で fo-dicom 5.2.6 との組合せを検証済み。.NET 8 への退避は不要）
- **fo-dicom 5.2.6**（MS-PL）/ Microsoft.Data.Sqlite（MIT）/ Serilog（Apache-2.0）
- 設定は **JSONC**（`System.Text.Json` + `JsonCommentHandling.Skip`）。YAML は使わない。
- SQLite は **WAL モード、ローカルディスクのみ**（ネットワーク共有に置くと破損する）
- 日本語エンコードのため、起動時に
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` を呼ぶこと。

## プロジェクト構成と参照の向き

```
BRIDGE-Navi 本体 ──▶ Ascube.Mwm.Abstractions ◀── Ascube.Mwm.Store
                                             ◀── Ascube.Mwm.Core ◀── Ascube.Mwm.Scp
                                                                  ◀── Ascube.Mwm.Tools
```

⚠ **BRIDGE-Navi 本体が参照してよいのは `Ascube.Mwm.Abstractions` と `Ascube.Mwm.Store` だけ。**
fo-dicom を本体に持ち込まない。

---

## よく使うコマンド

```bash
dotnet build
dotnet test                                    # 全単体・結合テスト
dotnet test --filter Category=Integration      # SCP を立てて叩くテスト

# サービスをコンソールで起動（開発時）
dotnet run --project src/Ascube.Mwm.Scp -- --console

# 設定の検証（SCP に触れない）
dotnet run --project src/Ascube.Mwm.Tools -- admin validate --profile BMD_HOLOGIC

# 装置なしで生成データセットを確認（dcmdump 形式＋16進）
dotnet run --project src/Ascube.Mwm.Tools -- scu preview --profile BMD_HOLOGIC --patient 000012345678

# 疎通・クエリ
dotnet run --project src/Ascube.Mwm.Tools -- scu echo --host 127.0.0.1 --port 11112
dotnet run --project src/Ascube.Mwm.Tools -- scu find --date today

# 0件の原因を特定
dotnet run --project src/Ascube.Mwm.Tools -- admin explain-query --run T07

# 独立検証（DCMTK。fo-dicom と別実装であることに意味がある）
echoscu -v -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 11112
findscu -v -d -W -k "(0040,0100)[0].(0040,0002)=20260928-20260929" \
        -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 11112
```

---

## 検証の注意

自作 SCP と自作 SCU は同じ fo-dicom を使う。**したがってライブラリ由来のバグは両方に等しく現れ、
自作テスタでは検出できない。**「規格として正しいか」の判定には必ず **DCMTK（C++・別実装）** を使うこと。
DMWLT4 も fo-dicom ベースなので同じ盲点を持つ。

`wlmscpfs` との比較は**タグ・VR・値・SQ入れ子・DIMSE status の意味比較**で行う。
バイナリ完全一致は Message ID / UID / PDU分割 / padding が正当に異なるため成立しない。

---

## 進め方

- **1タスク＝1ブランチ＝1セッション。**タスクが変わったら `/clear` するか新しいセッションを開く。
- **受入条件を先にテストコードにしてから実装する。**
  受入条件は `docs/実装指示書.md` の各タスク節にあり、テスト名にできる形で書かれている。
- **受入条件を満たさないまま次のタスクに進まない。**
- タスク完了ごとに `docs/progress.md` に1行追記する（日付・タスクID・受入条件の合否・残課題）。

### スケジュール

**実装に使えるのは 2026-09-04 〜 09-18 の11営業日**（9/19〜23 は5連休）。
9/18 機能凍結 → 9/24〜25 現地事前準備 → 9/28 現地接続テスト。

**9/11 終了時点で T5（MatchEngine）まで完了していること。**
遅れている場合は `docs/開発計画_v2.md` §6-1 の「作らない」リストを拡大して調整する。
