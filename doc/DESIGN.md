# LAN一時HTTPファイル共有ツール 設計書

**バージョン**: 1.0  
**作成日**: 2025-12-23  
**ステータス**: Draft

---

## 目次

1. [全体アーキテクチャ](#1-全体アーキテクチャ)
2. [ルーティング設計](#2-ルーティング設計)
3. [セキュリティ設計](#3-セキュリティ設計)
4. [ログ設計](#4-ログ設計)
5. [HTML設計](#5-html設計)
6. [停止/ライフサイクル設計](#6-停止ライフサイクル設計)
7. [例外設計](#7-例外設計)
8. [ディレクトリ/ファイル構成案](#8-ディレクトリファイル構成案)
9. [テスト戦略](#9-テスト戦略)
10. [ビルド/配布](#10-ビルド配布)

---

## 1. 全体アーキテクチャ

### 1.1 技術選定

| 項目            | 選定                             | 理由                                                                 |
| --------------- | -------------------------------- | -------------------------------------------------------------------- |
| 言語/ランタイム | C# / .NET 8                      | 単体exe出力可、Windows標準的、HTTP機能内蔵                           |
| HTTPサーバー    | `HttpListener`                   | 管理者権限不要（localhost以外も`netsh`不要でbind可）、軽量、依存なし |
| JSON処理        | `System.Text.Json`               | .NET標準、追加依存不要                                               |
| CLI解析         | 自前実装 or `System.CommandLine` | 依存最小化のため自前推奨                                             |
| ビルド形式      | Single-file self-contained       | Python不要環境対応、xcopy deploy                                     |

### 1.2 アーキテクチャ概要

```
┌─────────────────────────────────────────────────────────────┐
│                        lan-share.exe                        │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ CLI Parser  │→ │   Config    │→ │   HttpListener      │  │
│  └─────────────┘  └─────────────┘  │   (0.0.0.0:port)    │  │
│                                     └──────────┬──────────┘  │
│                                                │              │
│  ┌─────────────────────────────────────────────▼───────────┐ │
│  │                    Request Router                        │ │
│  │  /{token}/         → IndexHandler (HTML)                │ │
│  │  /{token}/dl       → DownloadHandler (Shared)           │ │
│  │  /{token}/udl      → DownloadHandler (Uploads)          │ │
│  │  /{token}/upload   → UploadHandler                      │ │
│  │  /{token}/browse   → BrowseHandler (AJAX JSON)          │ │
│  │  その他            → 404                                 │ │
│  └─────────────────────────────────────────────────────────┘ │
│                          │                                    │
│  ┌───────────┐  ┌───────▼───────┐  ┌──────────────────────┐ │
│  │ IdleTimer │  │ AccessLogger  │  │ LifecycleManager     │ │
│  │ TTLTimer  │  │ (JSONL+Rotate)│  │ (Ctrl+C/TTL/Idle)    │ │
│  └───────────┘  └───────────────┘  └──────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 1.3 主要コンポーネント

| コンポーネント     | 責務                                         |
| ------------------ | -------------------------------------------- |
| `Program`          | エントリポイント、CLI解析、初期化            |
| `Config`           | 設定値の保持・検証                           |
| `Server`           | HttpListener管理、リクエストディスパッチ     |
| `Router`           | URLパターンマッチ、ハンドラー振り分け        |
| `TokenValidator`   | capability URL検証                           |
| `PathValidator`    | パストラバーサル防止、シンボリックリンク検証 |
| `IndexHandler`     | HTML一覧ページ生成                           |
| `DownloadHandler`  | ファイルダウンロード処理                     |
| `UploadHandler`    | ファイルアップロード処理                     |
| `AccessLogger`     | JSONLログ出力、ローテーション                |
| `LifecycleManager` | TTL/Idle/Ctrl+C管理、シャットダウン          |

---

## 2. ルーティング設計

### 2.1 エンドポイント一覧

| メソッド | パス              | クエリ                                    | 説明                     | 成功時     | エラー時    |
| -------- | ----------------- | ----------------------------------------- | ------------------------ | ---------- | ----------- |
| GET      | `/{token}/`       | -                                         | トップページ（HTML一覧） | 200 + HTML | -           |
| GET      | `/{token}/browse` | `area=shared\|uploads`<br>`path=<relDir>` | ディレクトリJSON取得     | 200 + JSON | 400/404     |
| GET      | `/{token}/dl`     | `path=<relativePath>`                     | Sharedからダウンロード   | 200 + File | 400/404     |
| GET      | `/{token}/udl`    | `path=<relativePath>`                     | Uploadsからダウンロード  | 200 + File | 400/404     |
| POST     | `/{token}/upload` | `path=<relativeDir>`                      | Uploadsへアップロード    | 200 + JSON | 400/403/413 |
| *        | その他            | -                                         | 不正アクセス             | -          | 404         |

### 2.2 エンドポイント詳細

#### GET `/{token}/`
- **入力**: なし
- **出力**: HTML（インラインCSS/JS）
- **処理**: SharedとUploadsの2エリアを表示するHTML生成

#### GET `/{token}/browse`
- **入力**: 
  - `area`: `shared` or `uploads`
  - `path`: 相対ディレクトリパス（空 = ルート）
- **出力**:
```json
{
  "path": "subdir",
  "directories": [
    { "name": "child", "path": "subdir/child" }
  ],
  "files": [
    { "name": "file.txt", "path": "subdir/file.txt", "size": 1234, "modified": "2025-12-23T10:00:00Z" }
  ]
}
```
- **ステータス**: 200 / 400（不正パス） / 404（存在しない）

#### GET `/{token}/dl`
- **入力**: `path` = ROOT相対のファイルパス
- **出力**: ファイルバイナリ
- **ヘッダー**:
  - `Content-Type`: MIMEタイプ（判定不可時は `application/octet-stream`）
  - `Content-Disposition`: `attachment; filename="<name>"; filename*=UTF-8''<encoded>`
  - `Content-Length`: ファイルサイズ
- **ステータス**: 200 / 400（パス不正） / 404（ファイルなし）

#### GET `/{token}/udl`
- **入力**: `path` = UPLOAD_ROOT相対のファイルパス
- **出力**: `/{token}/dl` と同様
- **ステータス**: 同上

#### POST `/{token}/upload`
- **入力**: 
  - `path`: アップロード先ディレクトリ（UPLOAD_ROOT相対、空=直下）
  - Body: `multipart/form-data`（field名: `file`）
- **出力**:
```json
{
  "success": true,
  "files": [
    { "original": "report.pdf", "saved": "report (1).pdf", "size": 12345 }
  ]
}
```
- **ステータス**: 
  - 200: 成功
  - 400: パス不正/ファイルなし
  - 403: `--readonly` 時
  - 413: サイズ超過

### 2.3 トークン不一致時の挙動

- すべてのパスで `/{token}/` プレフィックスを検証
- 不一致時は **404 Not Found**（存在しないかのように振る舞う）
- レスポンスボディは空または汎用メッセージ

---

## 3. セキュリティ設計

### 3.1 Capability URL（トークン）

| 項目     | 仕様                                                       |
| -------- | ---------------------------------------------------------- |
| 生成方式 | `RandomNumberGenerator.GetBytes(18)` → Base64URL（24文字） |
| 文字種   | `[A-Za-z0-9_-]`（URL-safe Base64）                         |
| 長さ     | 24〜32文字（autoは24文字固定）                             |
| 検証     | 定数時間比較（タイミング攻撃対策）                         |
| 失敗時   | 404（トークン存在を秘匿）                                  |

```csharp
// トークン生成例
public static string GenerateToken()
{
    var bytes = new byte[18]; // 18 bytes = 24 Base64 chars
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes)
        .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
```

### 3.2 IP制限（--allow）

| 項目           | 仕様                                        |
| -------------- | ------------------------------------------- |
| 形式           | CIDR表記（例: `192.168.1.0/24,10.0.0.0/8`） |
| 未指定時       | 制限なし（全IP許可）                        |
| 検証タイミング | リクエスト受信直後（ハンドラー前）          |
| 拒否時         | 403 Forbidden                               |

```csharp
// CIDR判定ロジック
public bool IsAllowed(IPAddress remote)
{
    if (_allowedCidrs.Count == 0) return true;
    return _allowedCidrs.Any(cidr => cidr.Contains(remote));
}
```

### 3.3 パス検証（PathValidator）

#### 3.3.1 検証フロー

```
入力パス
   │
   ▼
┌─────────────────────────────────────┐
│ 1. 空文字/null チェック              │
│ 2. 絶対パス拒否 (Path.IsPathRooted) │
│ 3. ドライブレター拒否 (C: 等)        │
│ 4. UNCパス拒否 (\\ 開始)             │
│ 5. 禁止文字検出 (<>:"|?* NUL等)      │
│ 6. ".." セグメント拒否               │
└──────────────────┬──────────────────┘
                   ▼
┌─────────────────────────────────────┐
│ 7. Path.GetFullPath で正規化        │
│ 8. 結果がルート配下か検証            │
│    (StartsWith + ディレクトリ境界)   │
└──────────────────┬──────────────────┘
                   ▼
┌─────────────────────────────────────┐
│ 9. シンボリックリンク検証            │
│    - FileInfo.LinkTarget 確認       │
│    - リンク先がルート外なら拒否      │
└──────────────────┬──────────────────┘
                   ▼
                 許可
```

#### 3.3.2 実装ポイント

```csharp
public class PathValidator
{
    private readonly string _rootFullPath;
    
    public bool TryValidate(string relativePath, out string fullPath, out string error)
    {
        fullPath = null;
        error = null;
        
        // 基本検証
        if (string.IsNullOrEmpty(relativePath)) { /* root OK */ }
        if (Path.IsPathRooted(relativePath)) { error = "Absolute path"; return false; }
        if (relativePath.Contains("..")) { error = "Parent traversal"; return false; }
        if (relativePath.StartsWith(@"\\")) { error = "UNC path"; return false; }
        
        // 正規化＆境界チェック
        var combined = Path.Combine(_rootFullPath, relativePath);
        fullPath = Path.GetFullPath(combined);
        
        if (!fullPath.StartsWith(_rootFullPath + Path.DirectorySeparatorChar) 
            && fullPath != _rootFullPath)
        {
            error = "Outside root";
            return false;
        }
        
        // シンボリックリンク検証
        if (IsSymlinkOutsideRoot(fullPath)) { error = "Symlink outside root"; return false; }
        
        return true;
    }
}
```

### 3.4 アップロード制限

| 項目       | 仕様                                                             |
| ---------- | ---------------------------------------------------------------- |
| 最大サイズ | `--max-upload-mb`（デフォルト200MB）                             |
| 検証方法   | `Content-Length` ヘッダー事前検証 + ストリーム読み取り時カウント |
| 超過時     | 413 Payload Too Large（即座に接続切断）                          |
| ファイル名 | サニタイズ（`Path.GetInvalidFileNameChars()` 除去）              |
| 拡張子     | 制限なし（将来的にブラックリスト追加可）                         |

### 3.5 上書き防止

```csharp
public string GetSafeFileName(string directory, string fileName)
{
    var baseName = Path.GetFileNameWithoutExtension(fileName);
    var ext = Path.GetExtension(fileName);
    var candidate = fileName;
    var counter = 1;
    
    while (File.Exists(Path.Combine(directory, candidate)))
    {
        candidate = $"{baseName} ({counter}){ext}";
        counter++;
        if (counter > 10000) throw new InvalidOperationException("Too many duplicates");
    }
    
    return candidate;
}
```

### 3.6 _uploads ディレクトリ

| 項目               | 仕様                          |
| ------------------ | ----------------------------- |
| パス               | `{ROOT}\_uploads`             |
| 自動作成           | 初回アップロード時に作成      |
| Shared一覧での扱い | デフォルト非表示（除外）      |
| 直接アクセス       | `/udl` エンドポイント経由のみ |

---

## 4. ログ設計

### 4.1 ログ形式（JSONL）

1リクエスト = 1行のJSON

```json
{
  "ts": "2025-12-23T10:30:45.123+09:00",
  "rid": "a1b2c3d4",
  "ip": "192.168.1.100",
  "method": "GET",
  "endpoint": "/dl",
  "query": "path=docs/file.pdf",
  "area": "shared",
  "path": "docs/file.pdf",
  "status": 200,
  "bytes": 1048576,
  "ua": "Mozilla/5.0 ...",
  "dur": 123,
  "err": null
}
```

### 4.2 JSONスキーマ

| フィールド | 型               | 説明                             |
| ---------- | ---------------- | -------------------------------- |
| `ts`       | string (ISO8601) | タイムスタンプ（ローカルTZ）     |
| `rid`      | string           | リクエストID（8文字hex）         |
| `ip`       | string           | リモートIPアドレス               |
| `method`   | string           | HTTPメソッド                     |
| `endpoint` | string           | エンドポイント（トークン除去後） |
| `query`    | string           | クエリ文字列                     |
| `area`     | string?          | `shared` / `uploads` / null      |
| `path`     | string?          | 対象相対パス                     |
| `status`   | int              | HTTPステータスコード             |
| `bytes`    | long             | 送受信バイト数                   |
| `ua`       | string           | User-Agent                       |
| `dur`      | int              | 処理時間（ms）                   |
| `err`      | string?          | エラーメッセージ（あれば）       |

### 4.3 特殊ログエントリ

#### 起動ログ
```json
{
  "ts": "...",
  "event": "start",
  "version": "1.0.0",
  "root": "C:\\Share",
  "bind": "0.0.0.0",
  "port": 8000,
  "token": "abc...xyz",
  "ttl": 60,
  "idle": 30
}
```

#### 停止ログ
```json
{
  "ts": "...",
  "event": "stop",
  "reason": "ttl_expired|idle_timeout|user_interrupt|error",
  "uptime": 1800
}
```

### 4.4 ローテーション

| 項目       | 仕様                                      |
| ---------- | ----------------------------------------- |
| トリガー   | ログ書き込み前にサイズチェック            |
| 閾値       | 10MB                                      |
| 世代数     | 最大5（`.1` 〜 `.5`）                     |
| 命名       | `lan-share.log.1`, `lan-share.log.2`, ... |
| ローテ処理 | `.4`→`.5`, `.3`→`.4`, ... `.log`→`.1`     |

```csharp
public void RotateIfNeeded()
{
    var fi = new FileInfo(_logPath);
    if (!fi.Exists || fi.Length < 10 * 1024 * 1024) return;
    
    // 古いものから削除・リネーム
    for (int i = MaxGenerations; i >= 1; i--)
    {
        var src = $"{_logPath}.{i}";
        var dst = $"{_logPath}.{i + 1}";
        if (i == MaxGenerations && File.Exists(src)) File.Delete(src);
        else if (File.Exists(src)) File.Move(src, dst);
    }
    File.Move(_logPath, $"{_logPath}.1");
}
```

---

## 5. HTML設計

### 5.1 UI構成

```
┌────────────────────────────────────────────────────────────┐
│  LAN Share - {hostname}:{port}                    [TTL: 45m]│
├────────────────────────────────────────────────────────────┤
│                                                            │
│  ┌─ Shared Files ────────────────────────────────────────┐ │
│  │ 📁 Current: /docs                         [↑ Parent]  │ │
│  │ ┌────────────────────────────────────────────────────┐│ │
│  │ │ 📁 images/                                         ││ │
│  │ │ 📁 reports/                                        ││ │
│  │ │ 📄 readme.txt                    1.2 KB  [Download]││ │
│  │ │ 📄 data.xlsx                   245.3 KB  [Download]││ │
│  │ └────────────────────────────────────────────────────┘│ │
│  └───────────────────────────────────────────────────────┘ │
│                                                            │
│  ┌─ Uploads ─────────────────────────────────────────────┐ │
│  │ 📁 Current: /                                         │ │
│  │ ┌────────────────────────────────────────────────────┐│ │
│  │ │ 📄 uploaded_file.zip            12.5 MB  [Download]││ │
│  │ └────────────────────────────────────────────────────┘│ │
│  │                                                        │ │
│  │ ┌── Upload New File ────────────────────────────────┐ │ │
│  │ │ [Choose Files...] report.pdf              [Upload] │ │ │
│  │ │ Max: 200 MB                                        │ │ │
│  │ └────────────────────────────────────────────────────┘│ │
│  └───────────────────────────────────────────────────────┘ │
│                                                            │
│  ⓘ Access log: C:\Share\lan-share.log                     │
└────────────────────────────────────────────────────────────┘
```

### 5.2 HTMLテンプレート構造

```html
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>LAN Share</title>
  <style>
    /* インラインCSS（後述） */
  </style>
</head>
<body>
  <header>
    <h1>LAN Share - {hostname}:{port}</h1>
    <span id="ttl">TTL: {remaining}m</span>
  </header>
  
  <main>
    <section id="shared">
      <h2>📂 Shared Files</h2>
      <nav class="breadcrumb">...</nav>
      <div class="file-list" data-area="shared">...</div>
    </section>
    
    <section id="uploads">
      <h2>📤 Uploads</h2>
      <nav class="breadcrumb">...</nav>
      <div class="file-list" data-area="uploads">...</div>
      
      <form id="upload-form" enctype="multipart/form-data">
        <input type="file" name="file" multiple>
        <button type="submit">Upload</button>
        <small>Max: {maxUploadMb} MB</small>
      </form>
    </section>
  </main>
  
  <footer>
    <small>Log: {logPath}</small>
  </footer>
  
  <script>
    /* インラインJS（後述） */
  </script>
</body>
</html>
```

### 5.3 インラインCSS（最小限）

```css
* { box-sizing: border-box; }
body { font-family: system-ui, sans-serif; margin: 0; padding: 1rem; max-width: 900px; margin: auto; }
header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #ccc; padding-bottom: 0.5rem; }
section { margin: 1.5rem 0; padding: 1rem; border: 1px solid #ddd; border-radius: 8px; }
h2 { margin-top: 0; }
.breadcrumb { font-size: 0.9rem; margin-bottom: 0.5rem; }
.breadcrumb a { color: #0066cc; }
.file-list { border: 1px solid #eee; border-radius: 4px; max-height: 300px; overflow-y: auto; }
.file-item { display: flex; justify-content: space-between; padding: 0.5rem; border-bottom: 1px solid #f0f0f0; }
.file-item:hover { background: #f8f8f8; }
.file-item.dir { cursor: pointer; }
.file-name { flex: 1; }
.file-size { color: #666; margin: 0 1rem; }
.btn { padding: 0.25rem 0.75rem; border: 1px solid #0066cc; background: #fff; color: #0066cc; border-radius: 4px; cursor: pointer; text-decoration: none; }
.btn:hover { background: #0066cc; color: #fff; }
#upload-form { margin-top: 1rem; padding-top: 1rem; border-top: 1px solid #eee; }
#upload-form input[type="file"] { margin-right: 0.5rem; }
footer { font-size: 0.8rem; color: #666; margin-top: 2rem; }
```

### 5.4 インラインJS（機能）

```javascript
const TOKEN = '{token}';
const BASE = '/' + TOKEN;

// ディレクトリ読み込み
async function loadDir(area, path = '') {
  const res = await fetch(`${BASE}/browse?area=${area}&path=${encodeURIComponent(path)}`);
  if (!res.ok) { alert('Failed to load'); return; }
  const data = await res.json();
  renderFileList(area, data);
}

// ファイル一覧レンダリング
function renderFileList(area, data) {
  const container = document.querySelector(`.file-list[data-area="${area}"]`);
  container.innerHTML = '';
  
  // 親ディレクトリ
  if (data.path) {
    const parent = data.path.split('/').slice(0, -1).join('/');
    const item = createItem('📁', '..', () => loadDir(area, parent));
    container.appendChild(item);
  }
  
  // ディレクトリ
  data.directories.forEach(d => {
    const item = createItem('📁', d.name, () => loadDir(area, d.path));
    container.appendChild(item);
  });
  
  // ファイル
  const dlEndpoint = area === 'shared' ? 'dl' : 'udl';
  data.files.forEach(f => {
    const item = createFileItem(f, `${BASE}/${dlEndpoint}?path=${encodeURIComponent(f.path)}`);
    container.appendChild(item);
  });
}

// アップロード
document.getElementById('upload-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const formData = new FormData(e.target);
  const res = await fetch(`${BASE}/upload?path=`, { method: 'POST', body: formData });
  const result = await res.json();
  if (result.success) {
    alert(`Uploaded: ${result.files.map(f => f.saved).join(', ')}`);
    loadDir('uploads', '');
  } else {
    alert('Upload failed: ' + (result.error || 'Unknown error'));
  }
});

// 初期ロード
loadDir('shared', '');
loadDir('uploads', '');
```

---

## 6. 停止/ライフサイクル設計

### 6.1 ライフサイクル状態遷移

```
     ┌──────────────────────────────────────────────────────┐
     │                                                      │
     ▼                                                      │
┌─────────┐    ┌──────────┐    ┌─────────┐    ┌─────────┐  │
│  Init   │───▶│ Running  │───▶│Stopping │───▶│ Stopped │  │
└─────────┘    └──────────┘    └─────────┘    └─────────┘  │
     │              │                              │        │
     │              │ TTL/Idle/Ctrl+C              │        │
     │              ▼                              │        │
     │         ┌──────────┐                        │        │
     └────────▶│  Error   │────────────────────────┘        │
               └──────────┘                                  │
                    │                                        │
                    └────────────────────────────────────────┘
```

### 6.2 停止トリガー

| トリガー     | 条件                               | 動作                |
| ------------ | ---------------------------------- | ------------------- |
| TTL          | 起動から `--ttl` 分経過            | グレースフル停止    |
| Idle         | 最終リクエストから `--idle` 分経過 | グレースフル停止    |
| Ctrl+C       | `Console.CancelKeyPress`           | グレースフル停止    |
| 致命的エラー | ポートbind失敗等                   | 即時終了（コード1） |

### 6.3 タイマー実装

```csharp
public class LifecycleManager : IDisposable
{
    private readonly Timer _ttlTimer;
    private readonly Timer _idleTimer;
    private readonly CancellationTokenSource _cts;
    private DateTime _lastActivity;
    
    public event Action<StopReason> OnShutdown;
    
    public LifecycleManager(int ttlMinutes, int idleMinutes)
    {
        _cts = new CancellationTokenSource();
        _lastActivity = DateTime.UtcNow;
        
        // TTLタイマー（1回のみ）
        _ttlTimer = new Timer(_ => Shutdown(StopReason.TtlExpired), 
            null, TimeSpan.FromMinutes(ttlMinutes), Timeout.InfiniteTimeSpan);
        
        // Idleチェック（1分ごと）
        _idleTimer = new Timer(_ => CheckIdle(idleMinutes), 
            null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        // Ctrl+C
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; Shutdown(StopReason.UserInterrupt); };
    }
    
    public void RecordActivity() => _lastActivity = DateTime.UtcNow;
    
    private void CheckIdle(int idleMinutes)
    {
        if ((DateTime.UtcNow - _lastActivity).TotalMinutes >= idleMinutes)
            Shutdown(StopReason.IdleTimeout);
    }
    
    private void Shutdown(StopReason reason)
    {
        _cts.Cancel();
        OnShutdown?.Invoke(reason);
    }
}

public enum StopReason { TtlExpired, IdleTimeout, UserInterrupt, Error }
```

### 6.4 グレースフルシャットダウン

1. 新規リクエスト受付停止（`HttpListener.Stop()`）
2. 処理中リクエスト完了待機（最大5秒）
3. 停止理由をログ出力
4. 停止理由を標準出力
5. プロセス終了（コード0）

```csharp
public async Task ShutdownAsync(StopReason reason)
{
    Console.WriteLine($"Shutting down: {reason}");
    _logger.LogShutdown(reason, _startTime);
    
    _listener.Stop();
    
    // 処理中リクエスト待機
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try { await _pendingRequests.WaitAsync(cts.Token); }
    catch (OperationCanceledException) { /* タイムアウト */ }
    
    _listener.Close();
}
```

### 6.5 コンソール出力

```
LAN Share v1.0.0
────────────────────────────────────────
Root:    C:\Share
URL:     http://192.168.1.10:8000/xK9mP2...Yz/
Token:   xK9mP2...Yz (auto-generated)
TTL:     60 minutes
Idle:    30 minutes
Log:     C:\Share\lan-share.log
────────────────────────────────────────
Press Ctrl+C to stop.

[10:30:45] GET /dl?path=file.txt → 200 (1.2 MB)
[10:31:02] POST /upload → 200 (report.pdf → report (1).pdf)

Shutting down: idle_timeout (no activity for 30 minutes)
Goodbye.
```

---

## 7. 例外設計

### 7.1 例外分類

| カテゴリ       | 例外例               | ユーザー向け     | ログ                      | HTTPステータス  |
| -------------- | -------------------- | ---------------- | ------------------------- | --------------- |
| バリデーション | パス不正、サイズ超過 | 具体的理由       | Warning                   | 400/413         |
| 認可           | IP制限違反、readonly | 簡潔なエラー     | Warning                   | 403             |
| 存在           | ファイルなし         | "Not found"      | Info                      | 404             |
| サーバー       | IO例外、想定外       | "Internal error" | Error（スタックトレース） | 500             |
| 致命的         | ポートbind失敗       | 標準エラー出力   | Error                     | N/A（起動失敗） |

### 7.2 エラーレスポンス形式

```json
{
  "success": false,
  "error": "Path contains invalid characters",
  "code": "INVALID_PATH"
}
```

### 7.3 エラーコード一覧

| コード              | 説明                         | HTTP |
| ------------------- | ---------------------------- | ---- |
| `INVALID_PATH`      | パス検証失敗                 | 400  |
| `PATH_TRAVERSAL`    | パストラバーサル検出         | 400  |
| `SYMLINK_OUTSIDE`   | シンボリックリンクがルート外 | 400  |
| `FILE_NOT_FOUND`    | ファイルが存在しない         | 404  |
| `DIR_NOT_FOUND`     | ディレクトリが存在しない     | 404  |
| `PAYLOAD_TOO_LARGE` | アップロードサイズ超過       | 413  |
| `READONLY_MODE`     | 読み取り専用モード           | 403  |
| `IP_DENIED`         | IP制限による拒否             | 403  |
| `INTERNAL_ERROR`    | 内部エラー                   | 500  |

### 7.4 例外ハンドリングパターン

```csharp
public async Task HandleRequestAsync(HttpListenerContext ctx)
{
    var log = new AccessLog { Timestamp = DateTime.Now, RemoteIp = GetRemoteIp(ctx) };
    var sw = Stopwatch.StartNew();
    
    try
    {
        // ルーティング & 処理
        await _router.RouteAsync(ctx, log);
    }
    catch (ValidationException ex)
    {
        log.Error = ex.Message;
        log.StatusCode = ex.StatusCode;
        await WriteErrorAsync(ctx, ex.StatusCode, ex.Code, ex.Message);
    }
    catch (Exception ex)
    {
        log.Error = ex.ToString();
        log.StatusCode = 500;
        await WriteErrorAsync(ctx, 500, "INTERNAL_ERROR", "An internal error occurred");
        _logger.LogError(ex);  // スタックトレース含む
    }
    finally
    {
        log.DurationMs = (int)sw.ElapsedMilliseconds;
        _accessLogger.Log(log);
    }
}
```

---

## 8. ディレクトリ/ファイル構成案

### 8.1 プロジェクト構造

```
lan-share/
├── src/
│   └── LanShare/
│       ├── LanShare.csproj
│       ├── Program.cs                 # エントリポイント
│       │
│       ├── Config/
│       │   ├── AppConfig.cs           # 設定値クラス
│       │   └── CliParser.cs           # コマンドライン解析
│       │
│       ├── Server/
│       │   ├── HttpServer.cs          # HttpListener管理
│       │   ├── Router.cs              # ルーティング
│       │   └── RequestContext.cs      # リクエストコンテキスト
│       │
│       ├── Handlers/
│       │   ├── IHandler.cs            # ハンドラーインターフェース
│       │   ├── IndexHandler.cs        # トップページ
│       │   ├── BrowseHandler.cs       # ディレクトリ一覧JSON
│       │   ├── DownloadHandler.cs     # ダウンロード
│       │   └── UploadHandler.cs       # アップロード
│       │
│       ├── Security/
│       │   ├── TokenValidator.cs      # トークン検証
│       │   ├── PathValidator.cs       # パス検証
│       │   ├── IpFilter.cs            # IP制限
│       │   └── CidrRange.cs           # CIDR計算
│       │
│       ├── Logging/
│       │   ├── AccessLogger.cs        # アクセスログ
│       │   ├── AccessLogEntry.cs      # ログエントリ
│       │   └── LogRotator.cs          # ローテーション
│       │
│       ├── Lifecycle/
│       │   ├── LifecycleManager.cs    # TTL/Idle管理
│       │   └── StopReason.cs          # 停止理由enum
│       │
│       ├── Html/
│       │   └── TemplateEngine.cs      # HTML生成
│       │
│       └── Utils/
│           ├── MimeTypes.cs           # MIME判定
│           ├── FileNameSanitizer.cs   # ファイル名サニタイズ
│           └── NetworkUtils.cs        # ローカルIP取得等
│
├── tests/
│   └── LanShare.Tests/
│       ├── LanShare.Tests.csproj
│       ├── Security/
│       │   ├── PathValidatorTests.cs
│       │   ├── TokenValidatorTests.cs
│       │   └── IpFilterTests.cs
│       ├── Handlers/
│       │   ├── DownloadHandlerTests.cs
│       │   └── UploadHandlerTests.cs
│       └── Integration/
│           └── EndToEndTests.cs
│
├── doc/
│   └── DESIGN.md                      # 本設計書
│
├── .gitignore
├── README.md
└── lan-share.sln
```

### 8.2 クラス責務一覧

| クラス             | 責務                         | 主要メソッド                  |
| ------------------ | ---------------------------- | ----------------------------- |
| `Program`          | エントリポイント、DI組み立て | `Main()`                      |
| `AppConfig`        | 設定値保持・検証             | `Validate()`                  |
| `CliParser`        | コマンドライン解析           | `Parse(string[] args)`        |
| `HttpServer`       | HttpListener管理             | `StartAsync()`, `StopAsync()` |
| `Router`           | URL→ハンドラー振り分け       | `RouteAsync()`                |
| `IndexHandler`     | HTML生成・返却               | `HandleAsync()`               |
| `BrowseHandler`    | ディレクトリJSON返却         | `HandleAsync()`               |
| `DownloadHandler`  | ファイルストリーム返却       | `HandleAsync()`               |
| `UploadHandler`    | multipartパース・保存        | `HandleAsync()`               |
| `TokenValidator`   | トークン生成・検証           | `Generate()`, `Validate()`    |
| `PathValidator`    | パストラバーサル防止         | `TryValidate()`               |
| `IpFilter`         | CIDR判定                     | `IsAllowed()`                 |
| `AccessLogger`     | JSONL出力                    | `Log()`                       |
| `LogRotator`       | ローテーション実行           | `RotateIfNeeded()`            |
| `LifecycleManager` | TTL/Idle/Ctrl+C              | `RecordActivity()`            |

---

## 9. テスト戦略

### 9.1 テストピラミッド

```
         ┌───────────┐
         │ E2E Tests │  ← 最小限（実際のHTTP）
         │   (少)    │
        ─┴───────────┴─
       ┌───────────────┐
       │ Integration   │  ← ハンドラー単位
       │   Tests       │
      ─┴───────────────┴─
     ┌───────────────────┐
     │    Unit Tests     │  ← 大部分（ロジック）
     │      (多)         │
    ─┴───────────────────┴─
```

### 9.2 ユニットテスト

#### PathValidator
| ケース                     | 入力                    | 期待結果 |
| -------------------------- | ----------------------- | -------- |
| 正常パス                   | `docs/file.txt`         | OK       |
| 空パス（ルート）           | ``                      | OK       |
| 親ディレクトリ参照         | `../secret.txt`         | NG       |
| 絶対パス                   | `C:\Windows\system.ini` | NG       |
| UNCパス                    | `\\server\share`        | NG       |
| ドライブレター             | `D:file.txt`            | NG       |
| エンコード攻撃             | `..%2F..%2Fetc`         | NG       |
| 多重エンコード             | `%252e%252e%252f`       | NG       |
| NULバイト                  | `file.txt\0.jpg`        | NG       |
| シンボリックリンク（内部） | `link→内部dir`          | OK       |
| シンボリックリンク（外部） | `link→C:\Windows`       | NG       |

#### TokenValidator
| ケース       | 入力             | 期待結果 |
| ------------ | ---------------- | -------- |
| 一致         | 正しいトークン   | true     |
| 不一致       | 異なるトークン   | false    |
| 空           | ``               | false    |
| 部分一致     | トークン先頭のみ | false    |
| 大文字小文字 | 異なるケース     | false    |

#### IpFilter
| ケース           | 許可CIDR                   | リモートIP      | 期待結果 |
| ---------------- | -------------------------- | --------------- | -------- |
| 制限なし         | (未設定)                   | 任意            | true     |
| 単一IP一致       | `192.168.1.100/32`         | `192.168.1.100` | true     |
| 単一IP不一致     | `192.168.1.100/32`         | `192.168.1.101` | false    |
| サブネット一致   | `192.168.1.0/24`           | `192.168.1.50`  | true     |
| サブネット不一致 | `192.168.1.0/24`           | `192.168.2.50`  | false    |
| 複数CIDR一致     | `10.0.0.0/8,172.16.0.0/12` | `10.1.2.3`      | true     |

#### FileNameSanitizer
| ケース     | 入力               | 期待結果         |
| ---------- | ------------------ | ---------------- |
| 正常       | `report.pdf`       | `report.pdf`     |
| 禁止文字   | `file<>:".txt`     | `file.txt`       |
| パス区切り | `path/to/file.txt` | `pathtofile.txt` |
| 空白のみ   | `   `              | `unnamed`        |
| 予約名     | `CON.txt`          | `_CON.txt`       |

#### 上書き防止
| ケース   | 既存                       | 入力       | 期待結果       |
| -------- | -------------------------- | ---------- | -------------- |
| 重複なし | なし                       | `file.txt` | `file.txt`     |
| 1件重複  | `file.txt`                 | `file.txt` | `file (1).txt` |
| 複数重複 | `file.txt`, `file (1).txt` | `file.txt` | `file (2).txt` |

### 9.3 インテグレーションテスト

| ケース           | 手順                      | 検証                  |
| ---------------- | ------------------------- | --------------------- |
| ダウンロード正常 | GET /dl?path=test.txt     | 200, ファイル内容一致 |
| ダウンロード404  | GET /dl?path=notfound.txt | 404                   |
| アップロード正常 | POST /upload + ファイル   | 200, ファイル保存確認 |
| アップロード重複 | 同名ファイル2回UP         | 2回目は(1)付与        |
| サイズ超過       | 201MB ファイルUP          | 413                   |
| パストラバーサル | GET /dl?path=../secret    | 400                   |
| トークン不正     | GET /wrong-token/         | 404                   |
| IP制限           | 許可外IPから接続          | 403                   |

### 9.4 E2Eテスト

| シナリオ   | 手順                                                                |
| ---------- | ------------------------------------------------------------------- |
| 基本フロー | 1. 起動 2. ブラウザでアクセス 3. ファイルDL 4. ファイルUP 5. Ctrl+C |
| TTL停止    | TTL=1分で起動、1分後に自動停止確認                                  |
| Idle停止   | Idle=1分で起動、放置して1分後に自動停止確認                         |
| ポート探索 | 8000使用中に起動、8001でbind確認                                    |

### 9.5 テストツール

- **フレームワーク**: xUnit
- **モック**: NSubstitute または Moq
- **アサーション**: FluentAssertions
- **HTTPクライアント**: `HttpClient`（インテグレーション用）

---

## 10. ビルド/配布

### 10.1 プロジェクトファイル (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>lan-share</AssemblyName>
    <Version>1.0.0</Version>
    
    <!-- Single-file settings -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishTrimmed>true</PublishTrimmed>
    <TrimMode>link</TrimMode>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    
    <!-- Metadata -->
    <Product>LAN Share</Product>
    <Description>Temporary HTTP file sharing tool for LAN</Description>
    <Copyright>2025</Copyright>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- 依存パッケージなし（標準ライブラリのみ） -->
  </ItemGroup>
</Project>
```

### 10.2 ビルドコマンド

#### 開発ビルド
```powershell
dotnet build src/LanShare/LanShare.csproj -c Debug
```

#### リリースビルド（単体exe）
```powershell
dotnet publish src/LanShare/LanShare.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./publish
```

#### 出力
```
publish/
└── lan-share.exe    # 約15-25MB（トリム後）
```

### 10.3 テスト実行

```powershell
dotnet test tests/LanShare.Tests/LanShare.Tests.csproj -c Release --logger "console;verbosity=normal"
```

### 10.4 CI/CD（GitHub Actions例）

```yaml
name: Build and Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore
        run: dotnet restore
      
      - name: Build
        run: dotnet build -c Release --no-restore
      
      - name: Test
        run: dotnet test -c Release --no-build --verbosity normal
      
      - name: Publish
        run: dotnet publish src/LanShare/LanShare.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true -o ./publish
      
      - name: Upload Artifact
        uses: actions/upload-artifact@v4
        with:
          name: lan-share-win-x64
          path: ./publish/lan-share.exe
```

### 10.5 配布形態

| 形態    | ファイル                       | 用途                     |
| ------- | ------------------------------ | ------------------------ |
| 単体exe | `lan-share.exe`                | 通常配布（xcopy deploy） |
| ZIP     | `lan-share-v1.0.0-win-x64.zip` | GitHub Release           |

### 10.6 動作確認手順

```powershell
# 1. 基本起動
.\lan-share.exe --dir "C:\Share"

# 2. オプション全指定
.\lan-share.exe --dir "C:\Share" --port 9000 --bind 192.168.1.10 --ttl 120 --idle 60 --token mytoken123 --allow "192.168.1.0/24" --max-upload-mb 500 --log "C:\Logs\share.log" --open

# 3. 読み取り専用
.\lan-share.exe --dir "C:\Share" --readonly
```

---

## 付録

### A. CLI ヘルプ出力例

```
LAN Share v1.0.0 - Temporary HTTP file sharing for LAN

USAGE:
    lan-share.exe --dir <path> [options]

REQUIRED:
    --dir <path>           Root directory to share

OPTIONS:
    --port <n>             Port number (default: auto 8000-8100)
    --bind <ip>            Bind address (default: 0.0.0.0)
    --ttl <minutes>        Time-to-live (default: 60)
    --idle <minutes>       Idle timeout (default: 30)
    --token <string|auto>  URL token (default: auto)
    --allow <cidr,...>     Allowed IP ranges (default: all)
    --max-upload-mb <n>    Max upload size in MB (default: 200)
    --log <path>           Log file path (default: <dir>\lan-share.log)
    --open                 Open URL in browser on start
    --readonly             Disable uploads

EXAMPLES:
    lan-share.exe --dir "C:\Share"
    lan-share.exe --dir "C:\Share" --port 9000 --ttl 30
    lan-share.exe --dir "C:\Share" --allow "192.168.1.0/24,10.0.0.0/8"
```

### B. 将来拡張案

| 機能                    | 優先度 | 説明                        |
| ----------------------- | ------ | --------------------------- |
| HTTPS対応               | 中     | 自己署名証明書生成          |
| Basic認証               | 低     | --auth user:pass オプション |
| QRコード表示            | 低     | コンソールにASCII QR表示    |
| ドラッグ&ドロップ       | 低     | HTML UIでのD&Dアップロード  |
| プログレス表示          | 低     | 大ファイルの進捗表示        |
| フォルダZipダウンロード | 中     | ディレクトリを丸ごとZIP     |

---

**End of Document**
