# LAN Share 実装計画書

**バージョン**: 1.0  
**作成日**: 2025-12-23  
**関連文書**: [DESIGN.md](DESIGN.md)

---

## 目次

1. [MVP（最小実行可能版）の定義](#1-mvp最小実行可能版の定義)
2. [タスク分解](#2-タスク分解)
3. [各タスクの詳細](#3-各タスクの詳細)
4. [リスクと対策](#4-リスクと対策)
5. [テスト実装計画](#5-テスト実装計画)
6. [成果物一覧](#6-成果物一覧)
7. [実装上の注意](#7-実装上の注意)

---

## 1. MVP（最小実行可能版）の定義

### 1.1 MVPのスコープ

**含める機能（Must Have）:**

| 機能         | 説明                                     |
| ------------ | ---------------------------------------- |
| CLI基本      | `--dir` 必須、`--port` 任意（自動探索）  |
| トークン生成 | 24文字のcapability URL自動生成           |
| ファイル一覧 | Shared領域のディレクトリブラウズ（HTML） |
| ダウンロード | Shared領域からのファイルダウンロード     |
| パス検証     | パストラバーサル防止（基本）             |
| ログ         | JSONLアクセスログ（ローテなし）          |
| 停止         | Ctrl+Cでグレースフル停止                 |

**MVPで除外（Phase 2以降）:**

| 機能                   | 理由                             |
| ---------------------- | -------------------------------- |
| アップロード           | 複雑度が高い（multipart解析）    |
| `_uploads` 領域        | アップロード機能に依存           |
| IP制限                 | セキュリティ強化だが必須ではない |
| TTL/Idle自動停止       | Ctrl+Cで代替可能                 |
| ログローテーション     | 一時利用想定で優先度低           |
| `--open`               | 便利機能だが必須ではない         |
| `--readonly`           | アップロード未実装なら不要       |
| シンボリックリンク検証 | 高度なセキュリティ               |

### 1.2 MVP完了条件

```
✅ lan-share.exe --dir "C:\TestShare" で起動できる
✅ コンソールにURL（トークン付き）が表示される
✅ ブラウザでアクセスしてファイル一覧が見える
✅ ファイルをクリックしてダウンロードできる
✅ サブディレクトリに移動できる
✅ ../攻撃でルート外に出られない
✅ 不正トークンで404が返る
✅ アクセスログがJSONLで記録される
✅ Ctrl+Cで停止できる
```

### 1.3 MVPアーキテクチャ（簡略版）

```
lan-share.exe (MVP)
├── Program.cs           # エントリ + CLI解析
├── Config/
│   └── AppConfig.cs     # 設定保持
├── Server/
│   └── HttpServer.cs    # HttpListener + 簡易ルーティング
├── Security/
│   ├── TokenValidator.cs
│   └── PathValidator.cs
├── Handlers/
│   ├── IndexHandler.cs  # HTML一覧
│   └── DownloadHandler.cs
├── Logging/
│   └── AccessLogger.cs  # JSONL出力
└── Utils/
    └── MimeTypes.cs
```

---

## 2. タスク分解

### 2.1 フェーズ概要

```
Phase 0: プロジェクト初期化           [0.5h]
    ↓
Phase 1: MVP実装                      [4-5h]
    ├─ Task 1.1: CLI + Config
    ├─ Task 1.2: PathValidator
    ├─ Task 1.3: TokenValidator
    ├─ Task 1.4: AccessLogger
    ├─ Task 1.5: HttpServer + Router
    ├─ Task 1.6: IndexHandler (HTML)
    ├─ Task 1.7: DownloadHandler
    └─ Task 1.8: MVP統合テスト
    ↓
Phase 2: アップロード機能             [2-3h]
    ├─ Task 2.1: UploadHandler
    ├─ Task 2.2: FileNameSanitizer
    ├─ Task 2.3: _uploads領域対応
    └─ Task 2.4: HTML UI拡張
    ↓
Phase 3: ライフサイクル管理           [1-2h]
    ├─ Task 3.1: LifecycleManager
    ├─ Task 3.2: TTL/Idle実装
    └─ Task 3.3: --open実装
    ↓
Phase 4: セキュリティ強化             [1-2h]
    ├─ Task 4.1: IP制限 (--allow)
    ├─ Task 4.2: シンボリックリンク検証
    └─ Task 4.3: ログローテーション
    ↓
Phase 5: 仕上げ                       [1h]
    ├─ Task 5.1: --readonly対応
    ├─ Task 5.2: エラーハンドリング強化
    ├─ Task 5.3: README/ヘルプ
    └─ Task 5.4: リリースビルド
```

### 2.2 タスク依存関係

```
[Phase 0]
    │
    ▼
[1.1 CLI+Config] ──────────────────────────────┐
    │                                          │
    ├──▶ [1.2 PathValidator] ──┐               │
    │                          │               │
    ├──▶ [1.3 TokenValidator] ─┼──▶ [1.5 HttpServer+Router]
    │                          │               │
    └──▶ [1.4 AccessLogger] ───┘               │
                                               │
    ┌──────────────────────────────────────────┘
    │
    ├──▶ [1.6 IndexHandler] ──┬──▶ [1.8 MVP統合]
    │                         │
    └──▶ [1.7 DownloadHandler]┘
                              │
                              ▼
                    [Phase 2: Upload]
                              │
                              ▼
                    [Phase 3: Lifecycle]
                              │
                              ▼
                    [Phase 4: Security+]
                              │
                              ▼
                    [Phase 5: Polish]
```

---

## 3. 各タスクの詳細

### Phase 0: プロジェクト初期化

#### Task 0.1: ソリューション作成

| 項目 | 内容                                                                                          |
| ---- | --------------------------------------------------------------------------------------------- |
| 入力 | なし                                                                                          |
| 出力 | `lan-share.sln`, `src/LanShare/LanShare.csproj`, `tests/LanShare.Tests/LanShare.Tests.csproj` |
| 作業 | `dotnet new sln`, `dotnet new console`, `dotnet new xunit`, プロジェクト参照設定              |
| Done | `dotnet build` が成功、`dotnet test` が実行可能（テスト0件）                                  |

```powershell
# 実行コマンド例
dotnet new sln -n lan-share
dotnet new console -n LanShare -o src/LanShare
dotnet new xunit -n LanShare.Tests -o tests/LanShare.Tests
dotnet sln add src/LanShare tests/LanShare.Tests
dotnet add tests/LanShare.Tests reference src/LanShare
```

---

### Phase 1: MVP実装

#### Task 1.1: CLI + Config

| 項目     | 内容                                         |
| -------- | -------------------------------------------- |
| 入力     | `args[]`                                     |
| 出力     | `AppConfig` インスタンス                     |
| ファイル | `Config/AppConfig.cs`, `Config/CliParser.cs` |
| Done条件 | 下記テストケースがパス                       |

**テストケース:**
```
✅ --dir "C:\Share" → RootDir = "C:\Share"
✅ --dir 未指定 → エラー終了(code 1) + ヘルプ表示
✅ --port 9000 → Port = 9000
✅ --port 未指定 → Port = null (後で自動探索)
✅ --token mytoken → Token = "mytoken"
✅ --token auto → Token = null (後で自動生成)
✅ 存在しないディレクトリ → エラー終了
✅ --help → ヘルプ表示 + 終了(code 0)
```

**AppConfig プロパティ（MVP）:**
```csharp
public class AppConfig
{
    public string RootDir { get; set; }        // 必須
    public int? Port { get; set; }             // null = 自動探索
    public string BindAddress { get; set; } = "0.0.0.0";
    public string? Token { get; set; }         // null = 自動生成
    public string LogPath { get; set; }        // default: <RootDir>\lan-share.log
}
```

---

#### Task 1.2: PathValidator

| 項目     | 内容                                              |
| -------- | ------------------------------------------------- |
| 入力     | 相対パス文字列, ルートディレクトリ                |
| 出力     | `(bool isValid, string? fullPath, string? error)` |
| ファイル | `Security/PathValidator.cs`                       |
| Done条件 | 下記テストケースがパス                            |

**テストケース:**
```
✅ "docs/file.txt" → OK, fullPath = "C:\Root\docs\file.txt"
✅ "" (空) → OK, fullPath = "C:\Root"
✅ "../secret.txt" → NG, error = "PATH_TRAVERSAL"
✅ "..\..\Windows" → NG
✅ "C:\Windows\system.ini" → NG, error = "ABSOLUTE_PATH"
✅ "\\server\share" → NG, error = "UNC_PATH"
✅ "D:file.txt" → NG, error = "DRIVE_LETTER"
✅ "file<>.txt" → NG, error = "INVALID_CHARS"
✅ "subdir/../other" → 正規化してOK（ルート内なら）
✅ "subdir/../../out" → NG（ルート外）
```

**実装ポイント:**
- `Path.GetFullPath()` で正規化
- `StartsWith(rootPath + Path.DirectorySeparatorChar)` で境界チェック
- Windowsの禁止文字: `<>:"|?*` + 制御文字

---

#### Task 1.3: TokenValidator

| 項目     | 内容                                    |
| -------- | --------------------------------------- |
| 入力     | URLパス, 期待トークン                   |
| 出力     | `(bool isValid, string? remainingPath)` |
| ファイル | `Security/TokenValidator.cs`            |
| Done条件 | 下記テストケースがパス                  |

**テストケース:**
```
✅ GenerateToken() → 24文字, [A-Za-z0-9_-]のみ
✅ "/abc123.../dl" + token="abc123..." → true, remaining="/dl"
✅ "/wrong/dl" + token="abc123..." → false
✅ "/" + token="abc123..." → false
✅ "/abc123.../" + token="abc123..." → true, remaining="/"
✅ 定数時間比較（タイミング攻撃対策）
```

---

#### Task 1.4: AccessLogger

| 項目     | 内容                                                   |
| -------- | ------------------------------------------------------ |
| 入力     | `AccessLogEntry`                                       |
| 出力     | JSONLファイルへの追記                                  |
| ファイル | `Logging/AccessLogger.cs`, `Logging/AccessLogEntry.cs` |
| Done条件 | 下記テストケースがパス                                 |

**テストケース:**
```
✅ Log() → ファイルに1行JSON追記
✅ JSON形式が正しい（パース可能）
✅ 必須フィールドがすべて含まれる
✅ 複数回Log() → 複数行追記
✅ ファイルが存在しない → 作成
✅ 起動ログ・停止ログが書ける
```

**AccessLogEntry（MVP）:**
```csharp
public class AccessLogEntry
{
    public DateTime Timestamp { get; set; }
    public string? RequestId { get; set; }
    public string? RemoteIp { get; set; }
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public string? Query { get; set; }
    public int StatusCode { get; set; }
    public long BytesSent { get; set; }
    public string? UserAgent { get; set; }
    public int DurationMs { get; set; }
    public string? Error { get; set; }
}
```

---

#### Task 1.5: HttpServer + Router

| 項目     | 内容                                       |
| -------- | ------------------------------------------ |
| 入力     | `AppConfig`                                |
| 出力     | HTTPリクエスト受付・ルーティング           |
| ファイル | `Server/HttpServer.cs`, `Server/Router.cs` |
| Done条件 | 下記テストケースがパス                     |

**テストケース:**
```
✅ 指定ポートでbind成功
✅ ポート使用中 → 次ポート試行（8000-8100）
✅ 全ポート失敗 → エラー終了
✅ /{token}/ → IndexHandler呼び出し
✅ /{token}/dl → DownloadHandler呼び出し
✅ /wrongtoken/ → 404
✅ /other → 404
✅ Ctrl+C → グレースフル停止
```

**ルーティングテーブル（MVP）:**
```
GET  /{token}/      → IndexHandler
GET  /{token}/dl    → DownloadHandler
GET  /{token}/browse → BrowseHandler (AJAX用)
*    その他         → 404
```

---

#### Task 1.6: IndexHandler (HTML)

| 項目     | 内容                                                 |
| -------- | ---------------------------------------------------- |
| 入力     | HTTPリクエスト                                       |
| 出力     | HTML（ファイル一覧）                                 |
| ファイル | `Handlers/IndexHandler.cs`, `Html/TemplateEngine.cs` |
| Done条件 | 下記テストケースがパス                               |

**テストケース:**
```
✅ GET /{token}/ → 200 + HTML
✅ HTMLにファイル一覧が含まれる
✅ HTMLにサブディレクトリ一覧が含まれる
✅ ダウンロードリンクが正しい
✅ Content-Type: text/html; charset=utf-8
```

**HTML要件（MVP）:**
- インラインCSS/JS（外部ファイル不要）
- Shared領域のみ表示
- ディレクトリクリックでAJAXナビゲーション
- ファイルクリックでダウンロード

---

#### Task 1.7: DownloadHandler

| 項目     | 内容                                                |
| -------- | --------------------------------------------------- |
| 入力     | HTTPリクエスト, `path`クエリパラメータ              |
| 出力     | ファイルバイナリ                                    |
| ファイル | `Handlers/DownloadHandler.cs`, `Utils/MimeTypes.cs` |
| Done条件 | 下記テストケースがパス                              |

**テストケース:**
```
✅ GET /dl?path=file.txt → 200 + ファイル内容
✅ Content-Type: 適切なMIME
✅ Content-Disposition: attachment; filename="file.txt"
✅ Content-Length: ファイルサイズ
✅ path=../secret → 400 (PATH_TRAVERSAL)
✅ path=notfound.txt → 404
✅ path未指定 → 400
✅ 大ファイル（10MB+）→ ストリーミング成功
```

---

#### Task 1.8: MVP統合テスト

| 項目     | 内容                         |
| -------- | ---------------------------- |
| 入力     | 実際の起動・ブラウザアクセス |
| 出力     | E2E動作確認                  |
| Done条件 | 下記シナリオ成功             |

**E2Eシナリオ:**
```
1. テストディレクトリ作成（ファイル数個、サブディレクトリ）
2. lan-share.exe --dir "C:\TestShare" 起動
3. コンソールに表示されたURLをブラウザで開く
4. ファイル一覧が表示される
5. サブディレクトリをクリック → ナビゲーション
6. ファイルをクリック → ダウンロード成功
7. 不正トークンでアクセス → 404
8. ../攻撃 → 400
9. Ctrl+C → 停止ログ出力 + 終了
10. lan-share.log にアクセスログがある
```

---

### Phase 2: アップロード機能

#### Task 2.1: UploadHandler

| 項目     | 内容                        |
| -------- | --------------------------- |
| 入力     | multipart/form-data         |
| 出力     | JSON（成功/失敗）           |
| ファイル | `Handlers/UploadHandler.cs` |
| Done条件 | 下記テストケースがパス      |

**テストケース:**
```
✅ POST /upload + file → 200 + {"success": true, "files": [...]}
✅ ファイルが _uploads/ に保存される
✅ 複数ファイル同時UP
✅ 同名ファイル → name (1).ext で保存
✅ Content-Length > max → 413
✅ ファイルなし → 400
✅ 不正パス → 400
```

---

#### Task 2.2: FileNameSanitizer

| 項目     | 内容                         |
| -------- | ---------------------------- |
| 入力     | ファイル名                   |
| 出力     | サニタイズ済みファイル名     |
| ファイル | `Utils/FileNameSanitizer.cs` |
| Done条件 | 下記テストケースがパス       |

**テストケース:**
```
✅ "report.pdf" → "report.pdf"
✅ "file<>:.txt" → "file.txt"
✅ "path/to/file.txt" → "pathtofile.txt"
✅ "CON.txt" → "_CON.txt"
✅ "   " → "unnamed"
✅ "a" * 300 → 255文字に切り詰め
```

---

#### Task 2.3: _uploads領域対応

| 項目     | 内容                             |
| -------- | -------------------------------- |
| 入力     | -                                |
| 出力     | Uploads専用エンドポイント        |
| ファイル | `Handlers/BrowseHandler.cs` 拡張 |
| Done条件 | 下記テストケースがパス           |

**テストケース:**
```
✅ GET /browse?area=uploads → _uploads配下一覧
✅ GET /udl?path=file.txt → _uploads配下DL
✅ Shared一覧で _uploads フォルダが非表示
```

---

#### Task 2.4: HTML UI拡張

| 項目     | 内容                          |
| -------- | ----------------------------- |
| 入力     | -                             |
| 出力     | アップロードフォーム付きHTML  |
| ファイル | `Html/TemplateEngine.cs` 拡張 |
| Done条件 | 下記テストケースがパス        |

**テストケース:**
```
✅ SharedとUploads両方のセクション表示
✅ アップロードフォームが機能する
✅ アップロード後に一覧が更新される
✅ ファイル選択→Upload→成功メッセージ
```

---

### Phase 3: ライフサイクル管理

#### Task 3.1: LifecycleManager

| 項目     | 内容                                                       |
| -------- | ---------------------------------------------------------- |
| 入力     | TTL, Idle設定                                              |
| 出力     | タイマー管理、シャットダウンイベント                       |
| ファイル | `Lifecycle/LifecycleManager.cs`, `Lifecycle/StopReason.cs` |
| Done条件 | 下記テストケースがパス                                     |

**テストケース:**
```
✅ TTL経過 → OnShutdown発火
✅ Idle経過 → OnShutdown発火
✅ リクエスト受信 → Idleリセット
✅ Ctrl+C → OnShutdown発火
✅ 停止理由がログに記録される
```

---

#### Task 3.2: TTL/Idle実装

| 項目     | 内容                                          |
| -------- | --------------------------------------------- |
| 入力     | `--ttl`, `--idle`                             |
| 出力     | 自動停止                                      |
| ファイル | `Config/AppConfig.cs` 拡張, `Program.cs` 統合 |
| Done条件 | 下記テストケースがパス                        |

**テストケース:**
```
✅ --ttl 1 → 1分後に自動停止
✅ --idle 1 → 1分無操作で自動停止
✅ アクセスあり → Idleリセット
✅ 停止理由がコンソールに表示
```

---

#### Task 3.3: --open実装

| 項目     | 内容                   |
| -------- | ---------------------- |
| 入力     | `--open` フラグ        |
| 出力     | ブラウザ自動起動       |
| ファイル | `Program.cs`           |
| Done条件 | 下記テストケースがパス |

**テストケース:**
```
✅ --open → 起動後デフォルトブラウザでURL開く
✅ --open なし → ブラウザ起動しない
```

```csharp
// 実装例
Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
```

---

### Phase 4: セキュリティ強化

#### Task 4.1: IP制限 (--allow)

| 項目     | 内容                                            |
| -------- | ----------------------------------------------- |
| 入力     | CIDR文字列                                      |
| 出力     | IP検証                                          |
| ファイル | `Security/IpFilter.cs`, `Security/CidrRange.cs` |
| Done条件 | 下記テストケースがパス                          |

**テストケース:**
```
✅ --allow 未指定 → 全IP許可
✅ --allow "192.168.1.0/24" → 192.168.1.x のみ許可
✅ 許可外IP → 403
✅ 複数CIDR → カンマ区切り対応
✅ IPv4対応（IPv6は将来課題）
```

---

#### Task 4.2: シンボリックリンク検証

| 項目     | 内容                             |
| -------- | -------------------------------- |
| 入力     | ファイルパス                     |
| 出力     | リンク先がルート内か判定         |
| ファイル | `Security/PathValidator.cs` 拡張 |
| Done条件 | 下記テストケースがパス           |

**テストケース:**
```
✅ 通常ファイル → OK
✅ ルート内へのリンク → OK
✅ ルート外へのリンク → NG
✅ ジャンクション → 同様に検証
```

```csharp
// 実装例
var fi = new FileInfo(path);
if (fi.LinkTarget != null)
{
    var target = Path.GetFullPath(fi.LinkTarget);
    if (!target.StartsWith(rootPath)) return false;
}
```

---

#### Task 4.3: ログローテーション

| 項目     | 内容                         |
| -------- | ---------------------------- |
| 入力     | ログファイル                 |
| 出力     | ローテーション済みファイル群 |
| ファイル | `Logging/LogRotator.cs`      |
| Done条件 | 下記テストケースがパス       |

**テストケース:**
```
✅ 10MB未満 → ローテなし
✅ 10MB超 → .1 に退避
✅ .1 存在時 → .1→.2, .log→.1
✅ 最大5世代 → .5 は削除
```

---

### Phase 5: 仕上げ

#### Task 5.1: --readonly対応

| 項目     | 内容                                |
| -------- | ----------------------------------- |
| 入力     | `--readonly` フラグ                 |
| 出力     | アップロード無効化                  |
| ファイル | `Handlers/UploadHandler.cs`         |
| Done条件 | `--readonly` 時にPOST /upload → 403 |

---

#### Task 5.2: エラーハンドリング強化

| 項目     | 内容                                        |
| -------- | ------------------------------------------- |
| 入力     | 各種例外                                    |
| 出力     | 適切なエラーレスポンス・ログ                |
| ファイル | 全体                                        |
| Done条件 | 想定外例外でも500返却、スタックトレースログ |

---

#### Task 5.3: README/ヘルプ

| 項目     | 内容                         |
| -------- | ---------------------------- |
| 入力     | -                            |
| 出力     | `README.md`, CLIヘルプ       |
| ファイル | `README.md`                  |
| Done条件 | 使い方・オプション・例が記載 |

---

#### Task 5.4: リリースビルド

| 項目     | 内容                                   |
| -------- | -------------------------------------- |
| 入力     | -                                      |
| 出力     | `lan-share.exe` (single-file)          |
| Done条件 | 15-25MB程度の単体exe、他環境で動作確認 |

```powershell
dotnet publish src/LanShare/LanShare.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./publish
```

---

## 4. リスクと対策

### 4.1 技術リスク

| リスク                       | 影響         | 確率 | 対策                                                                        |
| ---------------------------- | ------------ | ---- | --------------------------------------------------------------------------- |
| HttpListenerで管理者権限要求 | 起動不可     | 低   | localhostでなくても`http://+:port/`でなければ不要。`http://*:port/`は避ける |
| ポート全使用中               | 起動不可     | 低   | 8000-8100の範囲でリトライ、エラーメッセージで明示                           |
| 大ファイルでメモリ不足       | クラッシュ   | 中   | ストリーミング転送、バッファサイズ制限（81920B）                            |
| multipart解析の複雑さ        | バグ         | 中   | 標準ライブラリなし→自前実装。boundary処理を慎重に                           |
| パストラバーサル見逃し       | セキュリティ | 中   | 多層防御（事前検証＋正規化＋最終境界チェック）                              |
| Windowsファイル名制限        | 保存失敗     | 低   | サニタイズ、予約名対応                                                      |

### 4.2 環境リスク

| リスク                             | 影響         | 確率 | 対策                                             |
| ---------------------------------- | ------------ | ---- | ------------------------------------------------ |
| ファイアウォールでブロック         | 接続不可     | 高   | ユーザーへの案内（一時的にFW無効化 or 例外追加） |
| アンチウイルスが検知               | exe実行不可  | 中   | 署名なし対策は難しい。ユーザーへの案内           |
| .NET未インストール                 | 起動不可     | 低   | self-contained ビルドで回避                      |
| 共有フォルダがネットワークドライブ | パス解決失敗 | 低   | UNCパス拒否で対応                                |

### 4.3 運用リスク

| リスク                     | 影響         | 確率 | 対策                               |
| -------------------------- | ------------ | ---- | ---------------------------------- |
| トークン漏洩               | 不正アクセス | 低   | TTL/Idleで自動停止、ログで追跡可能 |
| ログファイル肥大           | ディスク逼迫 | 低   | ローテーション（10MB×5世代）       |
| 長時間起動忘れ             | リソース占有 | 中   | TTL/Idle自動停止                   |
| アップロードでディスク逼迫 | 書き込み失敗 | 低   | --max-upload-mb で制限             |

### 4.4 対策優先度

```
必須（MVP）:
  ✅ パストラバーサル防止
  ✅ ストリーミング転送
  ✅ エラーハンドリング

推奨（Phase 2-3）:
  ✅ ファイル名サニタイズ
  ✅ アップロードサイズ制限
  ✅ TTL/Idle自動停止

任意（Phase 4）:
  ✅ IP制限
  ✅ シンボリックリンク検証
  ✅ ログローテーション
```

---

## 5. テスト実装計画

### 5.1 テストタイミング

| フェーズ | テスト種類  | タイミング                   |
| -------- | ----------- | ---------------------------- |
| Phase 0  | -           | ビルド確認のみ               |
| Phase 1  | Unit        | 各Task完了時                 |
| Phase 1  | Integration | Task 1.8                     |
| Phase 2  | Unit        | 各Task完了時                 |
| Phase 2  | Integration | Upload E2E                   |
| Phase 3  | Unit        | LifecycleManager             |
| Phase 3  | Integration | TTL/Idle動作確認             |
| Phase 4  | Unit        | IpFilter, シンボリックリンク |
| Phase 5  | E2E         | 全機能通し                   |

### 5.2 テストファイル構成

```
tests/LanShare.Tests/
├── LanShare.Tests.csproj
├── Config/
│   └── CliParserTests.cs
├── Security/
│   ├── PathValidatorTests.cs
│   ├── TokenValidatorTests.cs
│   └── IpFilterTests.cs
├── Logging/
│   ├── AccessLoggerTests.cs
│   └── LogRotatorTests.cs
├── Utils/
│   ├── FileNameSanitizerTests.cs
│   └── MimeTypesTests.cs
├── Handlers/
│   ├── DownloadHandlerTests.cs
│   └── UploadHandlerTests.cs
└── Integration/
    └── EndToEndTests.cs
```

### 5.3 テストユーティリティ

```csharp
// テスト用一時ディレクトリ
public class TempDirectory : IDisposable
{
    public string Path { get; }
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"lanshare_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }
    public void Dispose() => Directory.Delete(Path, true);
}
```

### 5.4 カバレッジ目標

| コンポーネント    | 目標 |
| ----------------- | ---- |
| PathValidator     | 90%+ |
| TokenValidator    | 100% |
| IpFilter          | 90%+ |
| FileNameSanitizer | 90%+ |
| Handlers          | 70%+ |
| 全体              | 75%+ |

---

## 6. 成果物一覧

### 6.1 ソースファイル

```
src/LanShare/
├── LanShare.csproj
├── Program.cs
├── Config/
│   ├── AppConfig.cs
│   └── CliParser.cs
├── Server/
│   ├── HttpServer.cs
│   └── Router.cs
├── Security/
│   ├── TokenValidator.cs
│   ├── PathValidator.cs
│   ├── IpFilter.cs
│   └── CidrRange.cs
├── Handlers/
│   ├── IHandler.cs
│   ├── IndexHandler.cs
│   ├── BrowseHandler.cs
│   ├── DownloadHandler.cs
│   └── UploadHandler.cs
├── Logging/
│   ├── AccessLogger.cs
│   ├── AccessLogEntry.cs
│   └── LogRotator.cs
├── Lifecycle/
│   ├── LifecycleManager.cs
│   └── StopReason.cs
├── Html/
│   └── TemplateEngine.cs
└── Utils/
    ├── MimeTypes.cs
    ├── FileNameSanitizer.cs
    └── NetworkUtils.cs
```

### 6.2 テストファイル

```
tests/LanShare.Tests/
├── LanShare.Tests.csproj
├── Config/
│   └── CliParserTests.cs
├── Security/
│   ├── PathValidatorTests.cs
│   ├── TokenValidatorTests.cs
│   └── IpFilterTests.cs
├── Logging/
│   ├── AccessLoggerTests.cs
│   └── LogRotatorTests.cs
├── Utils/
│   ├── FileNameSanitizerTests.cs
│   └── MimeTypesTests.cs
├── Handlers/
│   ├── DownloadHandlerTests.cs
│   └── UploadHandlerTests.cs
└── Integration/
    └── EndToEndTests.cs
```

### 6.3 ドキュメント

```
doc/
├── DESIGN.md          # 設計書（既存）
└── PLAN.md            # 本計画書

README.md              # 使い方、オプション、例
.gitignore             # ビルド成果物除外
```

### 6.4 ビルド成果物

```
publish/
└── lan-share.exe      # 単体exe（15-25MB）
```

---

## 7. 実装上の注意

### 7.1 Windowsパス処理

| 問題                  | 対策                                                        |
| --------------------- | ----------------------------------------------------------- |
| パス区切り `\` vs `/` | `Path.DirectorySeparatorChar` 使用、入力は両方許容          |
| 大文字小文字          | Windows = case-insensitive。比較時は `OrdinalIgnoreCase`    |
| 末尾スペース/ドット   | `Path.GetFullPath()` で正規化（Windowsは無視する）          |
| 予約語ファイル名      | `CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9` をサニタイズ |
| 長いパス              | 260文字制限。`\\?\` プレフィックスで回避可だが複雑          |
| ドライブレター        | `C:` 形式を拒否                                             |
| UNCパス               | `\\server\share` を拒否                                     |

**予約語チェック:**
```csharp
private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
{
    "CON", "PRN", "AUX", "NUL",
    "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
    "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
};

public static bool IsReservedName(string name)
{
    var baseName = Path.GetFileNameWithoutExtension(name);
    return ReservedNames.Contains(baseName);
}
```

### 7.2 HTTPヘッダー

| 問題               | 対策                                                                      |
| ------------------ | ------------------------------------------------------------------------- |
| 日本語ファイル名   | RFC 5987形式: `filename*=UTF-8''%E3%83%95%E3%82%A1%E3%82%A4%E3%83%AB.txt` |
| Content-Length不正 | 実際のファイルサイズ使用、ストリーム時は事前設定                          |
| MIME判定           | 拡張子ベース、不明時は `application/octet-stream`                         |

**Content-Disposition生成:**
```csharp
public static string GetContentDisposition(string fileName)
{
    var ascii = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(fileName));
    var utf8Encoded = Uri.EscapeDataString(fileName);
    return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{utf8Encoded}";
}
```

### 7.3 multipart解析

| 問題         | 対策                                                  |
| ------------ | ----------------------------------------------------- |
| boundary抽出 | `Content-Type: multipart/form-data; boundary=----xxx` |
| 大ファイル   | ストリーミング読み取り、一時ファイル不使用            |
| 複数ファイル | 連続処理                                              |
| 不正形式     | 厳密検証、エラー時は400                               |

**簡易multipart解析の流れ:**
```
1. Content-Typeからboundary取得
2. InputStream を --boundary で分割
3. 各パートのヘッダー解析（Content-Disposition）
4. filename 抽出
5. ボディ部分をファイルに書き込み
6. --boundary-- で終了
```

### 7.4 並行処理

| 問題                         | 対策                                |
| ---------------------------- | ----------------------------------- |
| 同時リクエスト               | HttpListenerは非同期対応済み        |
| ログ書き込み競合             | `lock` または `Channel<T>` で直列化 |
| ファイル書き込み競合         | 上書き防止で別名保存                |
| シャットダウン中のリクエスト | CancellationToken で中断、5秒待機   |

**ログ書き込みのスレッドセーフ化:**
```csharp
private readonly object _lock = new();

public void Log(AccessLogEntry entry)
{
    var json = JsonSerializer.Serialize(entry);
    lock (_lock)
    {
        File.AppendAllText(_path, json + Environment.NewLine);
    }
}
```

### 7.5 エンコーディング

| 問題           | 対策                                                  |
| -------------- | ----------------------------------------------------- |
| URLエンコード  | `Uri.EscapeDataString()` / `Uri.UnescapeDataString()` |
| HTMLエンコード | `System.Web.HttpUtility.HtmlEncode()` または手動      |
| JSONエンコード | `System.Text.Json` が自動処理                         |
| ファイル内容   | バイナリそのまま送信（Content-Type任せ）              |

### 7.6 リソース解放

| リソース                | 解放方法             |
| ----------------------- | -------------------- |
| HttpListener            | `Stop()` → `Close()` |
| FileStream              | `using` ブロック     |
| Timer                   | `Dispose()`          |
| CancellationTokenSource | `Dispose()`          |

```csharp
public async Task StopAsync()
{
    _listener.Stop();
    await Task.Delay(100); // 処理中リクエスト待機
    _listener.Close();
    _ttlTimer?.Dispose();
    _idleTimer?.Dispose();
}
```

---

## 付録

### A. コマンド早見表

```powershell
# ソリューション作成
dotnet new sln -n lan-share
dotnet new console -n LanShare -o src/LanShare
dotnet new xunit -n LanShare.Tests -o tests/LanShare.Tests
dotnet sln add src/LanShare tests/LanShare.Tests
dotnet add tests/LanShare.Tests reference src/LanShare

# ビルド
dotnet build

# テスト
dotnet test

# リリースビルド
dotnet publish src/LanShare/LanShare.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./publish

# 実行
./publish/lan-share.exe --dir "C:\TestShare"
```

### B. テスト用ディレクトリ構造例

```
C:\TestShare\
├── readme.txt
├── data.xlsx
├── docs/
│   ├── manual.pdf
│   └── specs/
│       └── spec.docx
├── images/
│   ├── logo.png
│   └── photo.jpg
└── _uploads/           # 自動作成
    └── uploaded.zip
```

### C. チェックリスト

**MVP完了チェック:**
- [ ] `dotnet build` 成功
- [ ] `dotnet test` 全パス
- [ ] `--dir` 必須検証
- [ ] ポート自動探索
- [ ] トークン生成・検証
- [ ] パストラバーサル防止
- [ ] ファイル一覧表示
- [ ] ファイルダウンロード
- [ ] アクセスログ出力
- [ ] Ctrl+C停止

**リリースチェック:**
- [ ] 全テストパス
- [ ] E2Eシナリオ成功
- [ ] 単体exe生成
- [ ] 別環境で動作確認
- [ ] README完成
- [ ] バージョン番号設定

---

**End of Document**
