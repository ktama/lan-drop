using System.Text;
using LanDrop.Config;

namespace LanDrop.Handlers;

/// <summary>
/// トップページHTML生成
/// </summary>
public static class IndexHandler
{
    public static string GenerateHtml(AppConfig config, string token, int port, int remainingMinutes)
    {
        var localIp = Utils.NetworkUtils.GetPrimaryLocalIp() ?? "localhost";
        var baseUrl = $"http://{localIp}:{port}/{token}";
        
        var html = $$"""
            <!DOCTYPE html>
            <html lang="ja">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>LAN Drop</title>
                <style>
                    * { box-sizing: border-box; }
                    body { 
                        font-family: system-ui, -apple-system, sans-serif; 
                        margin: 0; 
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
                    .btn-secondary { border-color: #666; color: #666; }
                    .btn-secondary:hover { background: #666; color: #fff; }
                    .upload-section { 
                        margin-top: 1rem; 
                        padding-top: 1rem; 
                        border-top: 1px solid #eee;
                    }
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
                    }
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
                    <div class="upload-section">
                        <form id="upload-form" class="upload-form" enctype="multipart/form-data">
                            <input type="file" name="file" id="file-input" multiple>
                            <button type="submit" class="btn">Upload</button>
                        </form>
                        <div class="upload-info">Max file size: {config.MaxUploadMb} MB</div>
                    </div>
                    """)}}
                </section>
                
                <footer>
                    <div>📝 Log: {{config.EffectiveLogPath}}</div>
                    <div style="margin-top: 0.25rem;">🔗 URL: {{baseUrl}}/</div>
                </footer>
                
                <script>
                    const TOKEN = '{{token}}';
                    const BASE = '/' + TOKEN;
                    const READONLY = {{(config.ReadOnly ? "true" : "false")}};
                    
                    let sharedPath = '';
                    let uploadsPath = '';
                    
                    function formatSize(bytes) {
                        if (bytes === 0) return '0 B';
                        const k = 1024;
                        const sizes = ['B', 'KB', 'MB', 'GB'];
                        const i = Math.floor(Math.log(bytes) / Math.log(k));
                        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
                    }
                    
                    function buildBreadcrumb(path, area) {
                        const parts = path ? path.split('/').filter(p => p) : [];
                        let html = '<a href="#" onclick="loadDir(\'' + area + '\', \'\'); return false;">Root</a>';
                        let currentPath = '';
                        parts.forEach(part => {
                            currentPath += (currentPath ? '/' : '') + part;
                            const cp = currentPath;
                            html += ' / <a href="#" onclick="loadDir(\'' + area + '\', \'' + cp + '\'); return false;">' + escapeHtml(part) + '</a>';
                        });
                        return html;
                    }
                    
                    function escapeHtml(text) {
                        const div = document.createElement('div');
                        div.textContent = text;
                        return div.innerHTML;
                    }
                    
                    async function loadDir(area, path) {
                        const listId = area + '-list';
                        const breadcrumbId = area + '-breadcrumb';
                        const list = document.getElementById(listId);
                        const breadcrumb = document.getElementById(breadcrumbId);
                        
                        list.innerHTML = '<div class="loading">Loading...</div>';
                        
                        try {
                            const res = await fetch(BASE + '/browse?area=' + area + '&path=' + encodeURIComponent(path));
                            if (!res.ok) {
                                throw new Error('Failed to load: ' + res.status);
                            }
                            const data = await res.json();
                            
                            if (area === 'shared') sharedPath = path;
                            else uploadsPath = path;
                            
                            breadcrumb.innerHTML = buildBreadcrumb(path, area);
                            renderFileList(area, data);
                        } catch (e) {
                            list.innerHTML = '<div class="error">' + escapeHtml(e.message) + '</div>';
                        }
                    }
                    
                    function renderFileList(area, data) {
                        const list = document.getElementById(area + '-list');
                        const dlEndpoint = area === 'shared' ? 'dl' : 'udl';
                        let html = '';
                        
                        // 親ディレクトリ
                        if (data.path) {
                            const parentPath = data.path.split('/').slice(0, -1).join('/');
                            html += '<div class="file-item dir" onclick="loadDir(\'' + area + '\', \'' + parentPath + '\')">';
                            html += '<div class="file-info"><span>📁</span><span class="file-name">..</span></div>';
                            html += '</div>';
                        }
                        
                        // ディレクトリ
                        data.directories.forEach(d => {
                            html += '<div class="file-item dir" onclick="loadDir(\'' + area + '\', \'' + escapeHtml(d.path) + '\')">';
                            html += '<div class="file-info"><span>📁</span><span class="file-name">' + escapeHtml(d.name) + '</span></div>';
                            html += '</div>';
                        });
                        
                        // ファイル
                        data.files.forEach(f => {
                            const dlUrl = BASE + '/' + dlEndpoint + '?path=' + encodeURIComponent(f.path);
                            html += '<div class="file-item">';
                            html += '<div class="file-info"><span>📄</span><span class="file-name">' + escapeHtml(f.name) + '</span></div>';
                            html += '<span class="file-size">' + formatSize(f.size) + '</span>';
                            html += '<a href="' + dlUrl + '" class="btn" download>Download</a>';
                            html += '</div>';
                        });
                        
                        if (!html) {
                            html = '<div class="empty">No files</div>';
                        }
                        
                        list.innerHTML = html;
                    }
                    
                    // アップロード処理
                    if (!READONLY) {
                        document.getElementById('upload-form')?.addEventListener('submit', async (e) => {
                            e.preventDefault();
                            const fileInput = document.getElementById('file-input');
                            const files = fileInput.files;
                            
                            if (files.length === 0) {
                                alert('Please select files to upload');
                                return;
                            }
                            
                            const formData = new FormData();
                            for (const file of files) {
                                formData.append('file', file);
                            }
                            
                            try {
                                const res = await fetch(BASE + '/upload?path=' + encodeURIComponent(uploadsPath), {
                                    method: 'POST',
                                    body: formData
                                });
                                
                                const result = await res.json();
                                
                                if (result.success) {
                                    const names = result.files.map(f => f.saved).join(', ');
                                    alert('Uploaded: ' + names);
                                    fileInput.value = '';
                                    loadDir('uploads', uploadsPath);
                                } else {
                                    alert('Upload failed: ' + (result.error || 'Unknown error'));
                                }
                            } catch (e) {
                                alert('Upload error: ' + e.message);
                            }
                        });
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
