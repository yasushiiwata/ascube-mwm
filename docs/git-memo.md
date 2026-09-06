# Git の使い方メモ

このプロジェクトでの Git 運用の実務的なルールをまとめたもの。判断に迷ったらここを見る。

---

## ブランチ運用

- **1タスク＝1ブランチ**（`CLAUDE.md` 進め方）。タスクが変わったらブランチも変える。
- 命名規則
  - 実装作業: `task/<内容>`（例: `task/T0-spikes`, `task/T0-alt4`）
  - ドキュメントのみの変更: `docs/<内容>`（例: `docs/v2-update`, `docs/alt4-finding`）
- ブランチは基本的に **`main` から作成する**（`git switch -c <branch>`）。
  直前のタスクブランチがまだ `main` に統合されていなくても、内容的に独立していれば
  `main` から切ってよい（無関係な未マージ変更を引きずらないため）。
- 作業を始める前に `git status` で作業ツリーがクリーンであることを確認する。
  汚れている場合は他人（または自分の前タスク）の途中経過の可能性があるので、
  勝手に消さずまず内容を確認する。

## コミット

- コミットメッセージは**日本語**。1行目は `種別: 要約`（`feat:` `docs:` `fix:` `merge:` など）。
- 本文には「何をしたか」ではなく**「なぜそうしたか」**を書く。差分は `git diff` で見れば分かる。
- Claude Code 経由のコミットには末尾に以下のトレーラーを付ける。
  ```
  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_...
  ```
- **1タスク＝1コミット**を基本とする。
- `git commit --amend` は使わない（明示的に頼まれたとき以外）。
  pre-commit hook で落ちた場合も、amend せず直して新しいコミットを積む。
- ステージングは `git add -A` で広く取ってもよいが、コミットする前に
  `git status` / `git diff --cached --stat` で**意図しないファイルが混ざっていないか必ず確認する**。

## マージ

- タスクブランチを `main` に統合するときは **`--no-ff`** を使い、
  どのタスクがいつ統合されたかを履歴に残す。
  ```bash
  git checkout main
  git merge --no-ff task/xxx -m "merge: ..."
  ```
- コンフリクトが起きたら中身を読んで解決する。`-X ours` / `-X theirs` で機械的に潰さない。
- マージ後は `git log --oneline --graph --all --decorate` で意図した形の履歴になっているか確認する。

## push

- **push は明示的に指示されたときだけ行う。**作業ごとに毎回 push しない。
- **force push（`--force` / `--force-with-lease`）は使わない。**特に `main` へは絶対に使わない。
- push 前に `git fetch` して `origin/main` との乖離がないか確認する
  （`git branch -vv` で `[origin/main]` の表示、ahead/behind を見る）。

## コミットしないもの（`.gitignore` と `CLAUDE.md` 規則14）

- 受診者データ・通信記録: `*.db` `*.db-wal` `*.db-shm` `*.pcapng` `*.pcap` `*.etl` `*.dcm` `*.wl`
- 依頼電文の実データ・レイアウト定義書: `*irai*.csv` `*.xlsx` `*.xls`
  - ⚠ `**/irai*.csv` は実ファイル名（例: `1irai20260701083008.csv`。数字始まり）に**マッチしない**。
    必ず `*irai*.csv` を使う。
- ビルド生成物・スパイクの出力: `bin/` `obj/` `spikes/**/out/`
- 迷ったら `git check-ignore -v <path>` でそのパスが除外設定にマッチするか確認する。
- 広く `git add` した後は、中身に個人情報や資格情報が紛れていないか
  ファイル名だけで判断せず確認する。

## よく使う確認コマンド

```bash
git status
git log --oneline --graph --all --decorate -20
git branch -vv
git remote -v
git check-ignore -v <path>                     # そのパスが .gitignore にマッチするか
git grep -n "<pattern>" <branch> -- <path>     # 特定ブランチの特定ファイルを検索
```

## 破壊的操作の扱い

- `git reset --hard` / `git checkout -- .` / `git clean -f` など、
  作業内容を消す可能性がある操作の前には、必ず `git status` を確認し、
  必要なら `git stash -u` で退避する。
- ブランチの強制削除（`git branch -D`）や履歴の書き換え（`rebase -i` 等）は、
  このプロジェクトでは基本的に行わない。
