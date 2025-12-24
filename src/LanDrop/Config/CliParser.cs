namespace LanDrop.Config;

/// <summary>
/// コマンドライン引数パーサー
/// </summary>
public static class CliParser
{
    public static (AppConfig? Config, string? Error, bool ShowHelp) Parse(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            return (null, null, true);
        }

        string? rootDir = null;
        int? port = null;
        string bindAddress = "0.0.0.0";
        int ttlMinutes = 60;
        int idleMinutes = 30;
        string? token = null;
        string? allowedCidrs = null;
        int maxUploadMb = 200;
        string? logPath = null;
        bool openBrowser = false;
        bool readOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--dir":
                    if (i + 1 >= args.Length) return (null, "--dir requires a path argument", false);
                    rootDir = args[++i];
                    break;

                case "--port":
                    if (i + 1 >= args.Length) return (null, "--port requires a number argument", false);
                    if (!int.TryParse(args[++i], out var p) || p < 1 || p > 65535)
                        return (null, "--port must be a valid port number (1-65535)", false);
                    port = p;
                    break;

                case "--bind":
                    if (i + 1 >= args.Length) return (null, "--bind requires an IP address argument", false);
                    bindAddress = args[++i];
                    break;

                case "--ttl":
                    if (i + 1 >= args.Length) return (null, "--ttl requires a number argument", false);
                    if (!int.TryParse(args[++i], out var ttl) || ttl < 1)
                        return (null, "--ttl must be a positive number", false);
                    ttlMinutes = ttl;
                    break;

                case "--idle":
                    if (i + 1 >= args.Length) return (null, "--idle requires a number argument", false);
                    if (!int.TryParse(args[++i], out var idle) || idle < 1)
                        return (null, "--idle must be a positive number", false);
                    idleMinutes = idle;
                    break;

                case "--token":
                    if (i + 1 >= args.Length) return (null, "--token requires a string argument", false);
                    var tokenArg = args[++i];
                    token = tokenArg == "auto" ? null : tokenArg;
                    break;

                case "--allow":
                    if (i + 1 >= args.Length) return (null, "--allow requires CIDR notation argument", false);
                    allowedCidrs = args[++i];
                    break;

                case "--max-upload-mb":
                    if (i + 1 >= args.Length) return (null, "--max-upload-mb requires a number argument", false);
                    if (!int.TryParse(args[++i], out var maxMb) || maxMb < 1)
                        return (null, "--max-upload-mb must be a positive number", false);
                    maxUploadMb = maxMb;
                    break;

                case "--log":
                    if (i + 1 >= args.Length) return (null, "--log requires a path argument", false);
                    logPath = args[++i];
                    break;

                case "--open":
                    openBrowser = true;
                    break;

                case "--readonly":
                    readOnly = true;
                    break;

                default:
                    if (arg.StartsWith("-"))
                        return (null, $"Unknown option: {arg}", false);
                    break;
            }
        }

        if (string.IsNullOrEmpty(rootDir))
        {
            return (null, "--dir is required", false);
        }

        if (!Directory.Exists(rootDir))
        {
            return (null, $"Directory does not exist: {rootDir}", false);
        }

        var config = new AppConfig
        {
            RootDir = Path.GetFullPath(rootDir),
            Port = port,
            BindAddress = bindAddress,
            TtlMinutes = ttlMinutes,
            IdleMinutes = idleMinutes,
            Token = token,
            AllowedCidrs = allowedCidrs,
            MaxUploadMb = maxUploadMb,
            LogPath = logPath,
            OpenBrowser = openBrowser,
            ReadOnly = readOnly
        };

        return (config, null, false);
    }

    public static string GetHelpText()
    {
        return """
            LAN Drop v1.0.0 - Temporary HTTP file sharing for LAN

            USAGE:
                lan-drop.exe --dir <path> [options]

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
                --log <path>           Log file path (default: <exe>\log\lan-drop.log)
                --open                 Open URL in browser on start
                --readonly             Disable uploads

            EXAMPLES:
                lan-drop.exe --dir "C:\Share"
                lan-drop.exe --dir "C:\Share" --port 9000 --ttl 30
                lan-drop.exe --dir "C:\Share" --allow "192.168.1.0/24,10.0.0.0/8"
            """;
    }
}
