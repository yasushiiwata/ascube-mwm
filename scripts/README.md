# scripts/

## 9/28 現地接続テストで使う運用スクリプト一覧（T12）

すべて `-ToolsExe <mwm-admin.exeのパス>` を渡せる（省略時は `dotnet run --project src/Ascube.Mwm.Tools --` で動く。開発機での動作確認用）。
配布物（`install-service.ps1` で publish したもの）を使う場合は `-ToolsExe C:\Ascube\Mwm\...\Ascube.Mwm.Tools.exe` のように渡す。

| スクリプト | 内容 |
|---|---|
| `00_watch.ps1` | **9/28に画面を見て真っ先に確認するもの。** 5秒間隔で「現在の受診者：あり／なし、TTL残 mm:ss」を大きく表示し続ける。 |
| `01_health.ps1` | `admin health` を1回だけ実行（単発確認）。 |
| `02_validate.ps1` | 現在の設定ファイルを検証する（起動前チェック・トラブル時の切り分け）。 |
| `03_echo.ps1` | 疎通確認（C-ECHO）だけを最速で行う。 |
| `04_find.ps1` | C-FIND を実行する。既定で `--emulate-apex-defaults`（Days Back 60 / Forward 2）を使う。 |
| `05_explain.ps1` | 「0件です」の原因を名指しで特定する（`admin explain-query`）。9/28で一番使う可能性が高いコマンド。 |
| `mark.bat` | `echoscu -aet MARK-STEP<N>` でpcap・生キャプチャ・監査ログに検索可能な目印を残す。使い方: `mark.bat 3` |
| `start-capture.bat` | dumpcap（別途インストールが必要。同梱しない）でのリングバッファpcapキャプチャ開始。 |
| `collect-evidence.ps1` | 証跡一式（プロファイル・直近監査ログ・T9生キャプチャ・pcap）を1フォルダにまとめる。 |
| `install-service.ps1` | Windows サービスとしてインストールする（要管理者権限。自動起動・異常終了時の自動復帰を設定）。 |
| `rollback.ps1` | `install-service.ps1` 実行前の状態へ戻す（要管理者権限。5分以内が目標）。 |
| `06_wlmscpfs_compare.ps1` | T13：DCMTK純正の参照実装 `wlmscpfs` と `mwm-scp` の応答をタグ・VR・値・SQ構造で意味比較する（UID・時刻等の可変値は除外）。使い方は次節。 |
| `07_set-current.ps1` | ⚠開発用。受診者をワークリストに1人セットする（`admin set-current`）。BRIDGE-Naviとの結線が無い/不調なときに、SCP-装置間だけの疎通を証明する手段。本番の投入経路ではない。使い方は次々節。 |
| `08_clear-current.ps1` | `07_set-current.ps1` の対。ワークリストを空にする（`admin clear-current`）。以後のC-FINDは0件+Success。 |

`install-service.ps1` / `rollback.ps1` はシステムへの変更（サービス登録・ACL）を伴うため、
このリポジトリの開発セッションでは自動実行していない。実機での実行前に内容を必ず確認すること。

### 00_watch.ps1 使用時の注意（文字化け）

`00_watch.ps1` は `mwm-admin` の出力を一度 PowerShell の変数に取り込んで判定するため、
Windows PowerShell 5.1 の既定エンコーディングのままだと日本語が文字化けして
「現在の受診者：あり」の判定を誤ることがある。このスクリプトは起動時に
`[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` を設定して対処済み。
他のスクリプト（01〜05, mark.bat）は出力をそのまま素通しするだけなのでこの問題は起きない。

## 06_wlmscpfs_compare.ps1（T13：wlmscpfs との意味比較）

`mwm-scp` を実際に起動した状態（別ターミナルで `dotnet run --project src/Ascube.Mwm.Scp -- --console --profile BMD_HOLOGIC`。
BRIDGE-Navi 側で受診者を1人 `SetCurrentAsync` しておくこと）で実行する。

```powershell
.\scripts\06_wlmscpfs_compare.ps1
```

DCMTK純正の `wlmscpfs`（別実装の参照 SCP）に同じ内容の受診者データを持つ `.wl` ファイルを1件用意し、
`mwm-scp` と `wlmscpfs` の両方に**同じ返却キー**で `findscu` を投げ、`dcmdump` 出力をタグ・VR・値・SQ構造で
比較する。UID・時刻・MessageID 等の可変値はマスクしてから比較するため、本質的な差分だけが残る
（実装指示書「禁止事項18：wlmscpfsとバイナリ完全一致を期待しない」に対応）。

### 判明した wlmscpfs の癖（ドキュメント未記載。本スクリプトはこれを踏まえて実装済み）

- **Called AE Title をそのままディレクトリ名として解釈する。** `-dfp <親ディレクトリ>` を指定し、
  Called AE Title と同名のサブディレクトリの中に `.wl` ファイルを置く必要がある
  （例：`-aec WLM_REF` なら `<dfp>\WLM_REF\*.wl`）。合わないと "Called AE Title Not Recognized" で
  アソシエーション自体を拒否される。
- そのサブディレクトリの中に**空の `lockfile` を事前に作っておく**必要がある
  （無いと `SetReadlock` エラーで起動時に読み込めない）。
- 既定（`-efr`）では「Type 1（必須・値あり）相当の属性が欠けている .wl ファイル」を黙って無視する。
  `ScheduledProcedureStepStartTime` と `ScheduledProcedureStepID` は**空文字列ではなく実際の値**を
  入れておくこと。
- `findscu` に返却キーを1つも渡さないと（`QueryRetrieveLevel` だけだと）空応答の送信に失敗して
  異常終了する。本スクリプトは全返却キーを空値で列挙した `query.dcm` を都度生成して渡している。

## 07_set-current.ps1 / 08_clear-current.ps1（開発用：BRIDGE-Navi抜きでの受診者投入）

**本番の受診者データ投入経路ではない。** `mwm-admin`/`mwm-scu` にはBRIDGE-Naviの代わりを
するコマンドが無かった（docs/連携テスト手順.md §2）ため追加した。BRIDGE-Naviとの結線が
まだ無い、または現地で結線が不調なときに、「SCPと装置の間は正常だ」を**BRIDGE-Navi抜きで**
証明するための道具。中身は `SqliteWorklistWriter.SetCurrentAsync`/`ClearCurrentAsync` を
CLIから直接呼ぶだけの薄いラッパー（本体コードとは別経路ではない）。

```powershell
# 受診者を1人セット（PatientIdは依頼電文 項番3「個人番号」12桁。★項番4「受診番号」ではない＝規則18）
.\scripts\07_set-current.ps1 -PatientId 000012345678 -FamilyKanji 武田 -GivenKanji 太郎 `
    -FamilyKana ﾀｹﾀﾞ -GivenKana ﾀﾛｳ -BirthDate 19600515 -Sex M -ProcedureDesc "骨密度(駅前)"

# 確認
.\scripts\01_health.ps1
.\scripts\04_find.ps1

# 片付け（次の受診者テストの前に必ず。前の受診者を残したまま次を投入しない＝規則16）
.\scripts\08_clear-current.ps1
```

`-Sex` は `M`/`F`/`O`（省略時は不明）。`-ScheduledDate` は省略時 `today`。
`-PatientId` が12桁の数字でない場合、`mwm-admin` 側で項番3/項番4取り違えの警告が出る。

## switch-charset.ps1（T10：ホットリロードによる文字コード切替）

稼働中の `mwm-scp` を**止めずに**、文字コード案①／②／④を切り替える。9/28 現地接続テストで、
初期案（①）が実機（Hologic APEX 5.6）と合わなかった場合の切り替え手順。

### 使い方

```powershell
# 稼働中の BMD_HOLOGIC プロファイルを案④（ISO_IR 13 単独・半角カナのみ）に切り替える
.\scripts\switch-charset.ps1 -Plan 4

# 案①に戻す
.\scripts\switch-charset.ps1 -Plan 1
```

- `-Plan`：1（初期値・ISO_IR 192／全角カナ+漢字+全角カナ）／2（ISO_IR 192／漢字+全角カナのみ）／4（ISO_IR 13 単独）
- `-ProfilePath`：省略時は `config/profiles/BMD_HOLOGIC.jsonc`。`mwm-scp` を別の `--profile` で
  起動している場合はそのプロファイルファイルを指定すること。

### 仕組み

`mwm-scp` は起動時に指定したプロファイルのファイル（`config/profiles/*.jsonc`）を
`FileSystemWatcher` で監視しており、保存を検知すると 500ms 待ってから自動的に再読込・再検証する。

1. `switch-charset.ps1` が対象ファイルの `"charset"` ブロックだけを書き換えて保存する。
2. `mwm-scp` が変更を検知し、再読込・再検証する。
3. **検証に成功した場合のみ**差し替える。次に受け付けるアソシエーションから新しい文字コードで応答する。
   進行中のアソシエーションは、アソシエーション確立時点の設定を最後まで使い続ける（切替の影響を受けない）。
4. 差し替え前の内容は `config/backup/<プロファイルID>.<タイムスタンプ>.jsonc` に自動保存される
   （手動で戻したい場合はこのファイルの内容を元のファイルに貼り戻す）。
5. 検証に失敗した場合は**稼働中の設定を維持する**（自動ロールバック）。`mwm-scp` のログに
   具体的な検証エラーが出るので、そこを見て `config/profiles/*.jsonc` を修正すること。

### 動作確認

```bash
# コンソールは CP932 で全角カナの表示が化けることがあるため、判定は dcmdump / 生バイトで行う。
findscu -v -k "0008,0052=WORKLIST" -aet TEST_SCU -aec ASCUBE_MWM 127.0.0.1 11112
```

応答の `(0008,0005) SpecificCharacterSet` と `(0010,0010) PatientName` の値で、
切替が反映されているか確認する。

### 既知の制約

- このスクリプトが差し替えるのは `charset` ブロックだけ（`network.aeTitle` / `network.port` は変えない）。
  待受ポートや AE Title 自体を変える場合は `mwm-scp` の再起動が必要（ホットリロードの対象外）。
- 検証対象は `mwm-scp` の起動時に `--profile` で渡したプロファイルIDのみ。
  ファイル名を変えたり別プロファイルIDを新規に追加しても、稼働中の待受には反映されない。
