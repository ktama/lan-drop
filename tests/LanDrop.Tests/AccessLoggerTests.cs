using LanDrop.Logging;
using Microsoft.AspNetCore.Http;

namespace LanDrop.Tests;

/// <summary>
/// AccessLogger のテスト
/// - JSONL形式でのログ出力
/// </summary>
public class AccessLoggerTests : IDisposable
{
    private readonly string _testDir;

    public AccessLoggerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "AccessLoggerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // 無視
        }
    }

    [Fact]
    public void Log_CreatesLogFile()
    {
        var logPath = Path.Combine(_testDir, "test.log");
        using var logger = new AccessLogger(logPath);
        
        var entry = new AccessLogEntry
        {
            RequestId = "test123",
            Method = "GET",
            Endpoint = "/test",
            StatusCode = 200
        };
        
        logger.Log(entry);
        
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public void Log_WritesJsonFormat()
    {
        var logPath = Path.Combine(_testDir, "json.log");
        using var logger = new AccessLogger(logPath);
        
        var entry = new AccessLogEntry
        {
            RequestId = "req123",
            Method = "GET",
            Endpoint = "/browse",
            StatusCode = 200,
            Bytes = 1024,
            DurationMs = 50
        };
        
        logger.Log(entry);
        logger.Dispose();
        
        var content = File.ReadAllText(logPath);
        // プロパティ名は短縮形（rid, method, endpoint, status等）
        Assert.Contains("\"rid\":\"req123\"", content);
        Assert.Contains("\"method\":\"GET\"", content);
        Assert.Contains("\"endpoint\":\"/browse\"", content);
        Assert.Contains("\"status\":200", content);
    }

    [Fact]
    public void Log_AppendsToExistingFile()
    {
        var logPath = Path.Combine(_testDir, "append.log");
        using var logger = new AccessLogger(logPath);
        
        logger.Log(new AccessLogEntry { RequestId = "first", Method = "GET", Endpoint = "/a", StatusCode = 200 });
        logger.Log(new AccessLogEntry { RequestId = "second", Method = "POST", Endpoint = "/b", StatusCode = 201 });
        logger.Dispose();
        
        var lines = File.ReadAllLines(logPath);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void LogStart_WritesStartEntry()
    {
        var logPath = Path.Combine(_testDir, "start.log");
        using var logger = new AccessLogger(logPath);
        
        logger.LogStart(new StartLogEntry
        {
            Root = "/share",
            Port = 8080,
            Token = "abc123",
            TtlMinutes = 60
        });
        logger.Dispose();
        
        var content = File.ReadAllText(logPath);
        Assert.Contains("\"event\":\"start\"", content);
        Assert.Contains("\"root\":\"/share\"", content);
        Assert.Contains("\"port\":8080", content);
    }

    [Fact]
    public void LogStop_WritesStopEntry()
    {
        var logPath = Path.Combine(_testDir, "stop.log");
        using var logger = new AccessLogger(logPath);
        
        logger.LogStop(new StopLogEntry
        {
            Reason = "TTL expired",
            UptimeSeconds = 3600
        });
        logger.Dispose();
        
        var content = File.ReadAllText(logPath);
        Assert.Contains("\"event\":\"stop\"", content);
        Assert.Contains("\"reason\":\"TTL expired\"", content);
        // プロパティ名は短縮形（uptime）
        Assert.Contains("\"uptime\":3600", content);
    }

    [Fact]
    public void CreateEntry_PopulatesFromHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.100");
        
        var entry = AccessLogger.CreateEntry(
            context,
            "/dl",
            "path=test.txt",
            "shared",
            "test.txt",
            200,
            1024,
            50);
        
        Assert.Equal("GET", entry.Method);
        Assert.Equal("/dl", entry.Endpoint);
        Assert.Equal("path=test.txt", entry.Query);
        Assert.Equal("shared", entry.Area);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(1024, entry.Bytes);
        Assert.Equal(50, entry.DurationMs);
        Assert.NotNull(entry.RequestId);
        Assert.Equal(8, entry.RequestId.Length);
    }

    [Fact]
    public void Log_NullValuesOmittedFromJson()
    {
        var logPath = Path.Combine(_testDir, "null.log");
        using var logger = new AccessLogger(logPath);
        
        var entry = new AccessLogEntry
        {
            RequestId = "test",
            Method = "GET",
            Endpoint = "/test",
            StatusCode = 200,
            Error = null,  // null値
            Query = null   // null値
        };
        
        logger.Log(entry);
        logger.Dispose();
        
        var content = File.ReadAllText(logPath);
        Assert.DoesNotContain("\"error\"", content);
        Assert.DoesNotContain("\"query\"", content);
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfNotExists()
    {
        var nestedDir = Path.Combine(_testDir, "nested", "dir");
        var logPath = Path.Combine(nestedDir, "test.log");
        
        using var logger = new AccessLogger(logPath);
        
        Assert.True(Directory.Exists(nestedDir));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var logPath = Path.Combine(_testDir, "dispose.log");
        var logger = new AccessLogger(logPath);
        
        logger.Dispose();
        logger.Dispose(); // 2回目も例外なし
    }

    [Fact]
    public void Log_AfterDispose_DoesNotThrow()
    {
        var logPath = Path.Combine(_testDir, "afterdispose.log");
        var logger = new AccessLogger(logPath);
        logger.Dispose();
        
        // Dispose後のログは書き込まれないが例外も発生しない
        var entry = new AccessLogEntry { RequestId = "test", Method = "GET", Endpoint = "/", StatusCode = 200 };
        var exception = Record.Exception(() => logger.Log(entry));
        
        Assert.Null(exception);
    }
}
