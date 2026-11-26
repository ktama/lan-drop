<#
.SYNOPSIS
    CHANGELOG.md の該当バージョンの節から GitHub Release の本文を作る。

.PARAMETER Version
    タグから "v" を除いたバージョン（例: 1.1.0、1.2.0-rc.1）

.PARAMETER OutFile
    出力先（Markdown）
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string]$Changelog = 'CHANGELOG.md'
)

$ErrorActionPreference = 'Stop'

$lines = [System.IO.File]::ReadAllLines((Resolve-Path $Changelog), [System.Text.Encoding]::UTF8)
$heading = '^## \[' + [regex]::Escape($Version) + '\]'

$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) { $start = $i + 1; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG.md に [$Version] の節がありません。リリース前に追記してください。"
    exit 1
}

# 次のバージョンの見出し、または末尾のリンク定義の手前までが本文
$section = New-Object System.Collections.Generic.List[string]
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## \[' -or $lines[$i] -match '^\[[^\]]+\]: ') { break }
    $section.Add($lines[$i])
}

# 前後の空行を詰める
while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[0])) { $section.RemoveAt(0) }
while ($section.Count -gt 0 -and [string]::IsNullOrWhiteSpace($section[$section.Count - 1])) { $section.RemoveAt($section.Count - 1) }

$notes = @(
    '## ダウンロード'
    ''
    '| プラットフォーム | ファイル |'
    '| ---------------- | -------- |'
    "| Windows x64      | ``lan-drop-$Version-win-x64.zip`` |"
    "| Windows x86      | ``lan-drop-$Version-win-x86.zip`` |"
    "| Windows ARM64    | ``lan-drop-$Version-win-arm64.zip`` |"
    ''
    'ZIP を展開し、`lan-drop.exe --dir <共有するフォルダ>` で起動します。'
    '詳しい使い方は同梱の README.md を参照してください。'
    ''
) + $section

[System.IO.File]::WriteAllLines($OutFile, [string[]]$notes, (New-Object System.Text.UTF8Encoding($false)))
