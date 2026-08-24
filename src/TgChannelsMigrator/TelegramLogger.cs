namespace TgChannelsMigrator;

/// <summary>
/// Redirects WTelegramClient's own protocol-level logging (the "Sending MsgContainer",
/// "Receiving RpcResult", ... lines) from the console to a log file, so the console only
/// shows this app's own menu/progress output.
/// </summary>
public static class TelegramLogger
{
    private static readonly string[] LevelNames = { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "CRIT" };
    private static readonly object WriteLock = new();

    public static void ConfigureFileLogging(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        WTelegram.Helpers.Log = (level, message) =>
        {
            var levelName = level >= 0 && level < LevelNames.Length ? LevelNames[level] : level.ToString();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{levelName}] {message}";
            lock (WriteLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        };
    }
}
