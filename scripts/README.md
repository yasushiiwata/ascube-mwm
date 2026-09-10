# scripts/

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
