# LAN Drop

LAN内で一時的にファイルを共有するための軽量HTTPサーバー。管理者権限不要、ブラウザのみで利用可能。

## 特徴

- 🔒 **Capability URL** - 24文字のトークン付きURLで安全にアクセス
- 📂 **ファイル共有** - ディレクトリ内のファイルを一覧・ダウンロード
- 📤 **アップロード対応** - ブラウザからドラッグ&ドロップでアップロード
- ⏱️ **自動停止** - TTL/アイドルタイムアウトで自動シャットダウン
- 🌐 **IP制限** - CIDR表記で接続元を制限可能
- 📋 **JSONLログ** - アクセスログを構造化形式で記録
- 📦 **単一exe** - 依存なしの自己完結型実行ファイル

## クイックスタート

```powershell
# カレントディレクトリを共有
landrop.exe

# 特定のディレクトリを共有
landrop.exe --dir C:\Share

# 読み取り専用モード
landrop.exe --readonly
```

起動すると以下のような出力が表示されます：

```
LAN Drop v1.0.0
════════════════════════════════════════════════════════════
  Root:     C:\Share
  URL:      http://192.168.1.10:8000/abc123def456ghi789jkl012/
  Token:    abc123def456ghi789jkl012 (auto-generated)
  TTL:      60 minutes
  Idle:     15 minutes
  Log:      access.jsonl
════════════════════════════════════════════════════════════
  Press Ctrl+C to stop.
```

表示されたURLを共有相手に伝えれば、ブラウザでアクセスできます。

## コマンドラインオプション

| オプション              | デフォルト            | 説明                       |
| ----------------------- | --------------------- | -------------------------- |
| `--dir <path>`          | カレントディレクトリ  | 共有するディレクトリ       |
| `--port <num>`          | 8000-8100で空きを探索 | 使用するポート番号         |
| `--ttl <min>`           | 60                    | 最大稼働時間（分）         |
| `--idle <min>`          | 15                    | アイドルタイムアウト（分） |
| `--token <str>`         | 自動生成              | URLトークン（24文字推奨）  |
| `--allow <cidr>`        | なし（全許可）        | 接続を許可するCIDR範囲     |
| `--max-upload-mb <num>` | 100                   | アップロード最大サイズ(MB) |
| `--log <path>`          | access.jsonl          | ログファイルパス           |
| `--open`                | false                 | 起動時にブラウザを開く     |
| `--readonly`            | false                 | アップロードを無効化       |
| `--help`                | -                     | ヘルプを表示               |

## 使用例

### IP制限付きで起動

```powershell
# 192.168.1.0/24からのみアクセス許可
landrop.exe --allow 192.168.1.0/24

# 複数のCIDR範囲を許可
landrop.exe --allow "10.0.0.0/8,192.168.0.0/16"
```

### 長時間共有

```powershell
# 8時間稼働、30分アイドルタイムアウト
landrop.exe --ttl 480 --idle 30
```

### ブラウザを自動で開く

```powershell
landrop.exe --open
```

### 固定トークン

```powershell
# 共有URLを固定したい場合
landrop.exe --token MySecretToken12345678
```

## ディレクトリ構成

```
共有ディレクトリ/
├── （ファイル群）      → ダウンロード可能
└── _uploads/         → アップロード先（自動作成）
```

- **Shared**: 指定したディレクトリ内のファイル（読み取り専用）
- **Uploads**: `_uploads/`ディレクトリ（読み書き可能）

## ログ形式

アクセスログはJSONL形式で出力されます：

```json
{"action":"start","ts":"2024-01-01T10:00:00.000Z","root":"C:\\Share","bind":"0.0.0.0","port":8000,"token":"abc...","ttl_min":60,"idle_min":15}
{"action":"access","ts":"2024-01-01T10:01:00.000Z","method":"GET","path":"/","client":"192.168.1.100","status":200,"size":1234,"ms":5}
{"action":"stop","ts":"2024-01-01T11:00:00.000Z","reason":"ttl","uptime_sec":3600}
```

ログは10MB超過時に自動ローテーション（最大5世代保持）されます。

## ビルド

### 必要環境

- .NET 8 SDK

### ビルドコマンド

```powershell
# 単一exe作成（Windows x64）
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true

# 出力場所
# src/landrop/bin/Release/net8.0/win-x64/publish/landrop.exe
```

### テスト実行

```powershell
dotnet test
```

## セキュリティ

- **Capability URL**: トークンを知らないとアクセス不可
- **パス検証**: ディレクトリトラバーサル攻撃を防止
- **IP制限**: CIDR表記で接続元を制限可能
- **自動停止**: 一定時間後に自動シャットダウン

### 注意事項

- LAN内での一時的な共有を想定しています
- 公開インターネットへの露出は推奨しません
- 機密ファイルの共有には適切な注意が必要です

## ライセンス

MIT License

## トラブルシューティング

### ポートが使用中

```
Error: Port 8000 is not available.
```

→ `--port`オプションで別のポートを指定してください。

### アクセスが拒否される

IP制限が設定されている場合、許可されたIPからのみアクセス可能です。
ログファイルを確認して接続元IPを確認してください。

### アップロードに失敗する

- `--readonly`オプションが有効になっていないか確認
- ファイルサイズが`--max-upload-mb`を超えていないか確認
- ディスク容量を確認
