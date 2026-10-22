# 検証記録

## Tokscale の実環境確認

- 実行環境: Windows 11 x64、npm 11.19.0
- 配布元・版: npm の `@tokscale/cli` 公開パッケージ、Tokscale 4.20.0
- 実行日時: 2026-10-20 08:27:19〜08:27:21 JST
- 実行コマンド: npm package内のWindows x64 `tokscale.exe usage --json`（プロセス直接起動）
- 結果: exit code 0、Codex entry 1件（`account` はnull）、Codex `5h` 83%、`Weekly` 23%、Grok `Weekly` 53%。リセット日時も取得できた。
- この実測でCodex 5h / Weekly と The X (Grok) Weeklyの値が確認できたため、Tokscaleを取得元として採用した。
- The X の週次利用枠は `Cursor` provider の `Weekly` metric から取得する。古いTokscale出力に `Cursor` がない場合は、互換性のため `Grok` provider の `Weekly` を使う。`Grok Monthly` は代用にしない。
- `Fixtures/tokscale-sanitized.json` はprovider、必要なmetric label、remaining percent、reset時刻だけを残したfixture。Tokscaleの生出力、アカウント識別情報、認証情報は含めていない。

## 自動検証

| 項目 | 結果 |
|---|---|
| JSON parser: Codex対象期間、Cursor優先のThe X週次枠、旧Grok形式互換、欠損、型不正、範囲外、複数Codexとactive選択、%切捨て | 合格 |
| CLI process: 非0終了、欠損実行ファイル、timeout時の子プロセス終了 | 合格 |
| 更新の重複防止、失敗時に前回値を現在値として残さない | 合格 |
| アイコン描画: 0 / 7 / 73 / 100 / `--`、100% / 150% / 200% / 450% / 500%相当の出力寸法、256px HICON元画像、非有限倍率拒否 | 合格 |
| Win32 HICON生成: 256px画像からの生成・破棄を複数の値で確認 | 合格 |
| Native icon所有権: 置換時に旧HICONを解放、Dispose時に現HICONを一度だけ解放 | 合格 |
| Message router: TaskbarButtonCreated / TaskbarCreated / WM_DPICHANGEDで再適用経路へ進むこと | 合格 |
| Window pinning policy: HWND未生成時の無処理、有効HWNDへの適用、Property Store失敗時の例外抑制 | 合格 |
| ドイツ語カルチャーで生成したJSONを実際に解析 | 合格 |
| Windows終了時の同期停止、実行中CLIのkill/wait有界経路 | 合格 |
| Closeでアプリ終了、タイトルバーの最小化でタスクバー格納 | 合格 |
| `dotnet test TaskbarAiUsage.Tests\TaskbarAiUsage.Tests.csproj -c Release` | 36件合格 |
| `dotnet build TaskbarAiUsage\TaskbarAiUsage.csproj -c Release` | 成功、0警告、0エラー |
| Windows x64 self-contained publish | 成功 |

今回の検証は .NET SDK 11.0.100-rc.2で実施した。SDKからプレビュー版使用の情報メッセージ（NETSDK1057）が表示された。Releaseビルドは警告・エラーなし。Windows x64 self-contained publishも成功した。発行先は `artifacts\publish\win-x64`。

## PR #2 Purpose Review の指摘対応

Purpose Review round 1 の5件のinline指摘（重複1件を含む、4テーマ）に対応した。

- アイコン倍率の任意上限を外し、0.5〜128倍を許容。非有限値・非正値と4096pxを超える寸法は拒否する。450% / 500%でそれぞれ144px / 160pxを生成する自動回帰テストを追加した。
- テストJSON生成をculture-invariantにし、`de-DE` カルチャで小数を含むJSONを構築してparserで実際に解析する回帰テストを追加した。
- `SessionEnding` は同期handlerに変更。終了時はタイマー停止・CancellationToken通知後、CLIをkillし最大2秒のProcess.WaitForExitで待つ。通常の「終了」は既存の非同期完了待ちを維持する。
- process timeoutを標準出力・標準エラー・親プロセス終了待ちの共通deadlineに適用した。回帰fixtureは親終了後も子が標準出力pipeを保持する状況を作り、timeout後に次の更新が進むことを確認する。テストのfinallyでfixtureの子プロセスを必ず終了し、一時PIDファイルを消す。

アイコン画像プレビューは `dotnet test` によりローカルの `artifacts\taskbar-icon-preview.png` に生成される。この画像とpublish出力は生成物であり、Git管理対象には含めない。

## 手動UI確認

目的は、実際のWindowsタスクバー上で通常アイコンの視認性と、最小化・再表示・終了の動きを確かめること。Codexの現在値は時間とともに変化するため、実行時の値がfixtureと同一であることは合否条件にしない。動的アイコンは実行中Windowを対象にする。タスクバーへのpinは不要で、固定ショートカットのアイコン更新は対象外。

この作業環境からWindowsネイティブのデスクトップUIを操作する手段は使えない。よって、発行したアプリをユーザーが初回起動するときに次を確認する。

1. `artifacts\publish\win-x64\TaskbarAiUsage.exe` を起動し、タスクバーとタイトルバーの両方に数値アイコンが表示されることを確認する。初回取得前の `-- / --` から取得後の数値に変わることも確認する。
2. アイコンの上段がCodex 5h、下段がThe X Weeklyであることを確認する。詳細画面でCodex WeeklyとThe X Weeklyを照合する。
3. 「更新」を押し、最終試行時刻が変わること、正常取得後に最終成功時刻と各値が更新されることを確認する。
4. タスクバーのボタンをクリックして詳細画面を開き、タイトルバーの最小化後に再表示できることを確認する。既存仕様では閉じるボタン / Alt+F4 / 「終了」はアプリ終了操作なので、プロセス継続の確認には最小化と再表示を使う。
5. Windowsを通常再起動し、ログイン後の自動起動を設定している場合は、タスクバーアイコンが描画され `-- / --` のまま残らず数値へ更新されることを確認する。
6. 必要と判断した場合のみ、手動でExplorerを再起動し、タスクバー復元後に動的アイコンが再表示されることを確認する。開発エージェントによるExplorer再起動は行わない。
7. 閉じるボタン、Alt+F4、「終了」のそれぞれでアプリを終了できることを確認する。

各項目の結果は次の表に日付と `合格` / `失敗` を記録する。失敗時は倍率・テーマ・再現手順・画面の様子も追記する。

| 実施日 | 通常起動/取得 | Windows再起動 | Explorer再起動（任意） | 詳細画面/最小化 | 終了操作 | 結果・メモ |
|---|---|---|---|---|---|---|
| 未実施 | — | — | — | — | — | 発行版を手動確認後に記録 |

### 失敗した場合

- アイコンの数字が切れる・小さすぎる場合: `TaskbarIconRenderer` の文字サイズと位置を調整し、テストとx64 publishをやり直す。
- タスクバー表示やClose挙動が崩れる場合: `ShowInTaskbar` とClose処理を修正して自動テストを追加し、手順を再実施する。
- 実行中のアプリはタイトルバーの最小化ボタンでタスクバーへ格納する。終了するときは閉じるボタン、Alt+F4、または詳細画面の「終了」を使う。このMVPは自動起動を登録しないため、アプリ終了後はタスクバーアイコンが消える。
- 設定を試す前に `appsettings.json` をコピーしておき、起動エラーが出たら元の内容へ戻す。
