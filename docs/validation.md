# 検証記録

## Tokscale の実環境確認

- 実行環境: Windows 11 x64、npm 11.19.0
- 配布元・版: npm の `@tokscale/cli` 公開パッケージ、Tokscale 4.20.0
- 実行日時: 2026-10-20 08:27:19〜08:27:21 JST
- 実行コマンド: npm package内のWindows x64 `tokscale.exe usage --json`（プロセス直接起動）
- 結果: exit code 0、Codex entry 1件（`account` はnull）、Codex `5h` 83%、`Weekly` 23%、Grok `Weekly` 53%。リセット日時も取得できた。
- この実測でCodex 5h / Weekly と The X (Grok) Weeklyの値が確認できたため、Tokscaleを取得元として採用した。
- `Cursor Weekly` や `Grok Monthly` はGrok Weeklyの代用にしない。
- `Fixtures/tokscale-sanitized.json` はprovider、必要なmetric label、remaining percent、reset時刻だけを残したfixture。Tokscaleの生出力、アカウント識別情報、認証情報は含めていない。

## 自動検証

| 項目 | 結果 |
|---|---|
| JSON parser: 対象provider / 期間、欠損、型不正、範囲外、複数Codexとactive選択、%切捨て | 合格 |
| CLI process: 非0終了、欠損実行ファイル、timeout時の子プロセス終了 | 合格 |
| 更新の重複防止、失敗時に前回値を現在値として残さない | 合格 |
| アイコン描画: 0 / 7 / 73 / 100 / `--`、100% / 150% / 200%相当の出力寸法 | 合格 |
| Closeを最小化として扱い、明示終了でのみCloseを許可するポリシー | 合格 |
| `dotnet test TaskbarAiUsage.Tests\TaskbarAiUsage.Tests.csproj -c Release` | 25件合格 |
| `dotnet build TaskbarAiUsage\TaskbarAiUsage.csproj -c Release` | 成功、0警告、0エラー |
| Windows x64 self-contained publish | 成功 |

この環境には安定版 .NET 10 SDK がなく、.NET 11 RC SDKで `net10.0-windows` をビルドした。ビルドと発行は成功したが、出力には「プレビュー版の .NET を使用しています」というSDK警告がある。安定版 .NET 10 SDKでの再実行は未確認。

アイコン画像プレビューは `dotnet test` によりローカルの `artifacts\taskbar-icon-preview.png` に生成される。この画像とpublish出力は生成物であり、Git管理対象には含めない。

## 手動UI確認

目的は、実際のWindowsタスクバー上で通常アイコンの視認性と、最小化・再表示・終了の動きを確かめること。Codexの現在値は時間とともに変化するため、実行時の値がfixtureと同一であることは合否条件にしない。

この作業環境からWindowsネイティブのデスクトップUIを操作する手段は使えない。よって、発行したアプリをユーザーが初回起動するときに次を確認する。

1. `artifacts\publish\win-x64\TaskbarAiUsage.exe` を起動し、タスクバーに通常のアプリアイコンが現れることを確認する。アプリは最小化状態で起動する。
2. アイコンの上段がCodex 5h、下段がGrok Weeklyであることを確認する。詳細画面でCodex WeeklyとGrok Weeklyを照合する。
3. タスクバーのボタンをクリックし、詳細画面が開くことを確認する。閉じるボタンとAlt+F4それぞれで最小化し、タスクバーアイコンが残ること、再クリックで戻ることを確認する。
4. 「更新」を押し、最終試行時刻が変わること、正常取得後に最終成功時刻と各値が更新されることを確認する。
5. 画面倍率100%、150%、200%で、ライトテーマとダークテーマをそれぞれ確認する。アイコンの数字が切れず、上下段を読み分けられることを確認する。
6. 「終了」を押し、タスクバーアイコンが消えてアプリが終了することを確認する。最後にWindowsのサインアウトまたはシャットダウンでもアプリが終了することを確認する。

各項目の結果は次の表に日付と `合格` / `失敗` を記録する。失敗時は倍率・テーマ・再現手順・画面の様子も追記する。

| 実施日 | 起動/取得 | タスクバー対応 | Close/Alt+F4/再表示 | 100% Light/Dark | 150% Light/Dark | 200% Light/Dark | 明示Exit/Windows終了 | 結果・メモ |
|---|---|---|---|---|---|---|---|---|
| 未実施 | — | — | — | — | — | — | — | Windows UIを手動確認後に記録 |

### 失敗した場合

- アイコンの数字が切れる・小さすぎる場合: `TaskbarIconRenderer` の文字サイズと位置を調整し、テストとx64 publishをやり直す。
- タスクバー表示やClose挙動が崩れる場合: `ShowInTaskbar` とClose処理を修正して自動テストを追加し、手順を再実施する。
- 実行中のアプリを止める場合は詳細画面の「終了」を使う。このMVPは自動起動を登録しないため、アプリを終了すれば元のタスクバー状態に戻る。
- 設定を試す前に `appsettings.json` をコピーしておき、起動エラーが出たら元の内容へ戻す。
