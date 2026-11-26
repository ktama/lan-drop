using System.Net;
using System.Security.Cryptography;
using LanDrop.Config;
using LanDrop.Utils;

namespace LanDrop.Handlers;

/// <summary>
/// トップページHTML生成
/// </summary>
/// <remarks>
/// ファイル名・フォルダ名は利用者が自由に付けられるため、HTML文字列の連結には使わない。
/// 一覧やパンくずリストは DOM API（textContent / addEventListener）で組み立てる。
/// インラインのスクリプトとスタイルは CSP の nonce で許可する。
/// </remarks>
public static class IndexHandler
{
    /// <summary>
    /// CSP 用の nonce を生成する
    /// </summary>
    public static string CreateNonce()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// トップページ用の Content-Security-Policy
    /// </summary>
    public static string ContentSecurityPolicy(string nonce)
        => $"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; " +
           "img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    /// <param name="config">設定</param>
    /// <param name="token">URLトークン（英数字と - _ のみ。CliParser で検証済み）</param>
    /// <param name="port">ポート</param>
    /// <param name="remainingMinutes">残りTTL（分）</param>
    /// <param name="displayHost">表示するホスト（ResolveDisplayHost で検証済み）</param>
    /// <param name="nonce">CSP の nonce（null の場合は nonce 属性を付けない）</param>
    public static string GenerateHtml(AppConfig config, string token, int port, int remainingMinutes, string displayHost, string? nonce = null)
    {
        var baseUrl = $"http://{displayHost}:{port}/{token}";
        var shareUrl = baseUrl + "/";
        var nonceAttr = nonce == null ? "" : $" nonce=\"{nonce}\"";
        var qrSvg = QrCode.Encode(shareUrl)?.ToSvg(pixelSize: 144) ?? "";
        var e = (string s) => WebUtility.HtmlEncode(s);

        var html = $$"""
            <!DOCTYPE html>
            <html lang="ja">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <meta name="referrer" content="no-referrer">
                <title>LAN Drop</title>
                <style{{nonceAttr}}>
                    * { box-sizing: border-box; }
                    body {
                        font-family: system-ui, -apple-system, sans-serif;
                        padding: 1rem;
                        max-width: 960px;
                        margin: 0 auto;
                        background: #f5f5f5;
                    }
                    header {
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        border-bottom: 2px solid #333;
                        padding-bottom: 0.5rem;
                        margin-bottom: 1rem;
                    }
                    h1 { margin: 0; font-size: 1.5rem; }
                    .ttl {
                        background: #333;
                        color: #fff;
                        padding: 0.25rem 0.75rem;
                        border-radius: 4px;
                        font-size: 0.9rem;
                    }
                    section {
                        margin: 1rem 0;
                        padding: 1rem;
                        background: #fff;
                        border: 1px solid #ddd;
                        border-radius: 8px;
                        box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    }
                    h2 {
                        margin: 0 0 0.75rem 0;
                        font-size: 1.2rem;
                        display: flex;
                        align-items: center;
                        gap: 0.5rem;
                    }
                    .breadcrumb {
                        font-size: 0.85rem;
                        margin-bottom: 0.5rem;
                        color: #666;
                    }
                    .breadcrumb a { color: #0066cc; text-decoration: none; }
                    .breadcrumb a:hover { text-decoration: underline; }
                    .file-list {
                        border: 1px solid #eee;
                        border-radius: 4px;
                        max-height: 350px;
                        overflow-y: auto;
                    }
                    .file-item {
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        padding: 0.5rem 0.75rem;
                        border-bottom: 1px solid #f0f0f0;
                    }
                    .file-item:last-child { border-bottom: none; }
                    .file-item:hover { background: #f8f8f8; }
                    .file-item.dir { cursor: pointer; }
                    .file-item.dir:hover { background: #e8f4ff; }
                    .file-info {
                        display: flex;
                        align-items: center;
                        gap: 0.5rem;
                        flex: 1;
                        min-width: 0;
                    }
                    .file-name {
                        overflow: hidden;
                        text-overflow: ellipsis;
                        white-space: nowrap;
                    }
                    .file-size {
                        color: #666;
                        font-size: 0.85rem;
                        white-space: nowrap;
                        margin-left: auto;
                        padding: 0 1rem;
                    }
                    .btn {
                        padding: 0.25rem 0.75rem;
                        border: 1px solid #0066cc;
                        background: #fff;
                        color: #0066cc;
                        border-radius: 4px;
                        cursor: pointer;
                        text-decoration: none;
                        font-size: 0.85rem;
                        white-space: nowrap;
                    }
                    .btn:hover { background: #0066cc; color: #fff; }
                    .btn:disabled { opacity: 0.5; cursor: default; }
                    .upload-section {
                        margin-top: 1rem;
                        padding: 1rem;
                        border: 2px dashed #ccc;
                        border-radius: 8px;
                    }
                    .upload-section.drag-over { border-color: #0066cc; background: #e8f4ff; }
                    .upload-form {
                        display: flex;
                        gap: 0.5rem;
                        align-items: center;
                        flex-wrap: wrap;
                    }
                    .upload-form input[type="file"] { flex: 1; min-width: 200px; }
                    .upload-info {
                        font-size: 0.8rem;
                        color: #666;
                        margin-top: 0.5rem;
                    }
                    .upload-progress { margin-top: 0.5rem; }
                    .upload-progress progress { width: 100%; height: 1rem; }
                    .upload-progress[hidden] { display: none; }
                    .empty {
                        padding: 2rem;
                        text-align: center;
                        color: #999;
                    }
                    footer {
                        font-size: 0.8rem;
                        color: #666;
                        margin-top: 1rem;
                        padding-top: 1rem;
                        border-top: 1px solid #ddd;
                        display: flex;
                        gap: 1rem;
                        align-items: center;
                        flex-wrap: wrap;
                    }
                    .footer-text div + div { margin-top: 0.25rem; }
                    .qr svg { display: block; }
                    .readonly-badge {
                        background: #ff9800;
                        color: #fff;
                        padding: 0.15rem 0.5rem;
                        border-radius: 3px;
                        font-size: 0.75rem;
                        margin-left: 0.5rem;
                    }
                    .loading {
                        text-align: center;
                        padding: 1rem;
                        color: #666;
                    }
                    .error {
                        color: #d32f2f;
                        padding: 0.5rem;
                        background: #ffebee;
                        border-radius: 4px;
                    }
                </style>
            </head>
            <body>
                <header>
                    <h1>📁 LAN Drop</h1>
                    <span class="ttl" id="ttl">TTL: {{remainingMinutes}}m</span>
                </header>

                <section id="shared-section">
                    <h2>📂 Shared Files</h2>
                    <nav class="breadcrumb" id="shared-breadcrumb">/</nav>
                    <div class="file-list" id="shared-list">
                        <div class="loading">Loading...</div>
                    </div>
                </section>

                <section id="uploads-section">
                    <h2>
                        📤 Uploads
                        {{(config.ReadOnly ? "<span class=\"readonly-badge\">READ ONLY</span>" : "")}}
                    </h2>
                    <nav class="breadcrumb" id="uploads-breadcrumb">/</nav>
                    <div class="file-list" id="uploads-list">
                        <div class="loading">Loading...</div>
                    </div>
                    {{(config.ReadOnly ? "" : $"""
                    <div class="upload-section" id="drop-zone">
                        <form id="upload-form" class="upload-form" enctype="multipart/form-data">
                            <input type="file" name="file" id="file-input" multiple>
                            <button type="submit" class="btn" id="upload-button">Upload</button>
                        </form>
                        <div class="upload-info">Drag &amp; drop files here, or choose files. Max file size: {config.MaxUploadMb} MB</div>
                        <div class="upload-progress" id="upload-progress" hidden>
                            <progress id="upload-bar" max="100" value="0"></progress>
                            <div class="upload-info" id="upload-status"></div>
                        </div>
                    </div>
                    """)}}
                </section>

                <footer>
                    <div class="qr" title="Scan to open on a phone">{{qrSvg}}</div>
                    <div class="footer-text">
                        <div>🔗 URL: {{e(shareUrl)}}</div>
                        <div>📝 Log: {{e(config.EffectiveLogPath)}}</div>
                        <div>LAN Drop v{{e(AppInfo.Version)}}</div>
                    </div>
                </footer>

                <script{{nonceAttr}}>
                    const TOKEN = '{{token}}';
                    const BASE = '/' + TOKEN;
                    const READONLY = {{(config.ReadOnly ? "true" : "false")}};
                    const MAX_UPLOAD_BYTES = {{config.MaxUploadBytes}};

                    const currentPath = { shared: '', uploads: '' };

                    function formatSize(bytes) {
                        if (bytes === 0) return '0 B';
                        const k = 1024;
                        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
                        const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1);
                        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
                    }

                    // 要素を作る（文字列は必ず textContent で入れる）
                    function el(tag, className, text) {
                        const node = document.createElement(tag);
                        if (className) node.className = className;
                        if (text !== undefined) node.textContent = text;
                        return node;
                    }

                    function navLink(text, handler) {
                        const a = el('a', null, text);
                        a.href = '#';
                        a.addEventListener('click', ev => { ev.preventDefault(); handler(); });
                        return a;
                    }

                    function renderBreadcrumb(area, path) {
                        const nav = document.getElementById(area + '-breadcrumb');
                        nav.replaceChildren(navLink('Root', () => loadDir(area, '')));
                        let acc = '';
                        for (const part of (path ? path.split('/').filter(p => p) : [])) {
                            acc = acc ? acc + '/' + part : part;
                            const target = acc;
                            nav.append(' / ', navLink(part, () => loadDir(area, target)));
                        }
                    }

                    function dirRow(name, onOpen) {
                        const row = el('div', 'file-item dir');
                        const info = el('div', 'file-info');
                        info.append(el('span', null, '📁'), el('span', 'file-name', name));
                        row.append(info);
                        row.addEventListener('click', onOpen);
                        return row;
                    }

                    function fileRow(area, f) {
                        const endpoint = area === 'shared' ? 'dl' : 'udl';
                        const row = el('div', 'file-item');
                        const info = el('div', 'file-info');
                        info.append(el('span', null, '📄'), el('span', 'file-name', f.name));
                        const link = el('a', 'btn', 'Download');
                        link.href = BASE + '/' + endpoint + '?path=' + encodeURIComponent(f.path);
                        link.setAttribute('download', '');
                        row.append(info, el('span', 'file-size', formatSize(f.size)), link);
                        return row;
                    }

                    function renderFileList(area, data) {
                        const list = document.getElementById(area + '-list');
                        const rows = [];

                        if (data.path) {
                            const parent = data.path.split('/').slice(0, -1).join('/');
                            rows.push(dirRow('..', () => loadDir(area, parent)));
                        }
                        for (const d of data.directories) {
                            rows.push(dirRow(d.name, () => loadDir(area, d.path)));
                        }
                        for (const f of data.files) {
                            rows.push(fileRow(area, f));
                        }
                        if (rows.length === 0) {
                            rows.push(el('div', 'empty', 'No files'));
                        }
                        list.replaceChildren(...rows);
                    }

                    async function loadDir(area, path) {
                        const list = document.getElementById(area + '-list');
                        list.replaceChildren(el('div', 'loading', 'Loading...'));

                        try {
                            const res = await fetch(BASE + '/browse?area=' + area + '&path=' + encodeURIComponent(path));
                            if (!res.ok) {
                                throw new Error('Failed to load: ' + res.status);
                            }
                            const data = await res.json();
                            currentPath[area] = path;
                            renderBreadcrumb(area, path);
                            renderFileList(area, data);
                        } catch (err) {
                            list.replaceChildren(el('div', 'error', err.message));
                        }
                    }

                    // アップロード（進捗表示・ドラッグ＆ドロップ）
                    function uploadFiles(files) {
                        if (!files || files.length === 0) {
                            alert('Please select files to upload');
                            return;
                        }
                        const tooLarge = Array.from(files).find(f => f.size > MAX_UPLOAD_BYTES);
                        if (tooLarge) {
                            alert('File too large: ' + tooLarge.name + ' (max ' + formatSize(MAX_UPLOAD_BYTES) + ')');
                            return;
                        }

                        const formData = new FormData();
                        for (const file of files) {
                            formData.append('file', file);
                        }

                        const progress = document.getElementById('upload-progress');
                        const bar = document.getElementById('upload-bar');
                        const status = document.getElementById('upload-status');
                        const button = document.getElementById('upload-button');
                        progress.hidden = false;
                        bar.value = 0;
                        status.textContent = 'Uploading...';
                        button.disabled = true;

                        const xhr = new XMLHttpRequest();
                        xhr.open('POST', BASE + '/upload?path=' + encodeURIComponent(currentPath.uploads));
                        xhr.upload.addEventListener('progress', ev => {
                            if (ev.lengthComputable) {
                                bar.value = Math.round(ev.loaded / ev.total * 100);
                                status.textContent = formatSize(ev.loaded) + ' / ' + formatSize(ev.total);
                            }
                        });
                        xhr.addEventListener('loadend', () => {
                            button.disabled = false;
                            let result = null;
                            try { result = JSON.parse(xhr.responseText); } catch { }

                            if (xhr.status === 200 && result && result.success) {
                                bar.value = 100;
                                status.textContent = 'Uploaded: ' + result.files.map(f => f.saved).join(', ');
                                document.getElementById('file-input').value = '';
                                loadDir('uploads', currentPath.uploads);
                            } else if (xhr.status === 0) {
                                status.textContent = 'Upload error: connection failed';
                            } else if (xhr.status === 413) {
                                status.textContent = 'Upload failed: file too large (max ' + formatSize(MAX_UPLOAD_BYTES) + ')';
                            } else {
                                status.textContent = 'Upload failed: ' + ((result && result.error) || ('HTTP ' + xhr.status));
                            }
                        });
                        xhr.send(formData);
                    }

                    if (!READONLY) {
                        document.getElementById('upload-form').addEventListener('submit', ev => {
                            ev.preventDefault();
                            uploadFiles(document.getElementById('file-input').files);
                        });

                        const zone = document.getElementById('drop-zone');
                        ['dragenter', 'dragover'].forEach(type => zone.addEventListener(type, ev => {
                            ev.preventDefault();
                            zone.classList.add('drag-over');
                        }));
                        ['dragleave', 'drop'].forEach(type => zone.addEventListener(type, ev => {
                            ev.preventDefault();
                            zone.classList.remove('drag-over');
                        }));
                        zone.addEventListener('drop', ev => uploadFiles(ev.dataTransfer.files));
                    }

                    // 初期ロード
                    loadDir('shared', '');
                    loadDir('uploads', '');
                </script>
            </body>
            </html>
            """;

        return html;
    }
}
