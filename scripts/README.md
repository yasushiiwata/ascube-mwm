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

`install-service.ps1` / `rollback.ps1` はシステムへの変更（サービス登録・ACL）を伴うため、
このリポジトリの開発セッションでは自動実行していない。実機での実行前に内容を必ず確認すること。

### 00_watch.ps1 使用時の注意（文字化け）

`00_watch.ps1` は `mwm-admin` の出力を一度 PowerShell の変数に取り込んで判定するため、
Windows PowerShell 5.1 の既定エンコーディングのままだと日本語が文字化けして
「現在の受診者：あり」の判定を誤ることがある。このスクリプトは起動時に
`[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` を設定して対処済み。
他のスクリプト（01〜05, mark.bat）は出力をそのまま素通しするだけなのでこの問題は起きない。

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
