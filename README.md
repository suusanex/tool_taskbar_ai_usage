# Taskbar AI Usage

Codex と The X / Grok の利用枠を、Windows タスクバーの通常のアプリアイコンへ表示する個人用ツールです。

```text
上段: Codex 5h の残量（%の数字）
下段: The X / Grok Weekly の残量（%の数字）
```

Codex Weekly は詳細画面に表示します。未取得または更新失敗時は該当する値を `--` にし、前回値を現在値として残しません。

## セットアップ

1. Node.js と npm をインストールします。
2. Tokscale をインストールします。

   ```powershell
   npm install --global @tokscale/cli
   tokscale usage --json
   ```

   Tokscale 側の案内に従い、Codex と The X の利用情報を読み取れる状態にします。アプリはTokscaleの既存認証を利用し、独自に認証情報を保存しません。

3. .NET 10 SDK を使い、Windows x64 向けに発行します。

   ```powershell
   dotnet publish .\TaskbarAiUsage\TaskbarAiUsage.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64
   ```

4. `artifacts\publish\win-x64\TaskbarAiUsage.exe` を起動します。起動時に最小化され、タスクバーに通常のアプリアイコンが表示されます。

## 使い方

- タスクバーのアイコンをクリックすると詳細画面を開きます。
- タイトルバーの最小化ボタンでウインドウをタスクバーへ格納し、タスクバーのアイコンをクリックすると再表示できます。
- 閉じるボタンまたは Alt+F4 でアプリを終了します。
- 「更新」はその場で一度だけTokscaleを実行します。更新中の重複実行は行いません。
- 「終了」でアプリを終了します。Windowsのサインアウト・シャットダウンでも終了します。
- 詳細画面には Codex 5h / Weekly、The X Weekly、各リセット日時（ローカル時刻）、最終試行・成功時刻、失敗概要が表示されます。

## 設定

発行先の `appsettings.json` を編集し、アプリを再起動すると反映されます。

```json
{
  "updateIntervalSeconds": 300,
  "cliTimeoutSeconds": 60,
  "tokscalePath": null
}
```

`updateIntervalSeconds` は定期更新間隔（30〜86400秒）、`cliTimeoutSeconds` はTokscaleのタイムアウト（1〜600秒）です。`tokscalePath` はTokscaleのWindowsネイティブ実行ファイルへの絶対パスです。省略または `null` の場合、npmのWindows x64ネイティブパッケージを標準配置から探します。

設定に誤りがある場合は、起動時に説明を表示します。実行時の安全な概要ログは `%LOCALAPPDATA%\TaskbarAiUsage\app.log` に記録されます。Tokscaleの生出力、stdout/stderr、認証情報はログへ記録しません。

## 開発

```powershell
dotnet test .\TaskbarAiUsage.Tests\TaskbarAiUsage.Tests.csproj -c Release
dotnet build .\TaskbarAiUsage\TaskbarAiUsage.csproj -c Release
```

Windows上のGUI操作が必要な手動確認手順と、実測したTokscale出力のサニタイズ済みfixtureは [`docs/validation.md`](docs/validation.md) にあります。
