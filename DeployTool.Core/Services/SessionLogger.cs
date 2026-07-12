using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Writes each event immediately (not just at the end) to Logs\{computer}_{timestamp}.log on the share.</summary>
public sealed class SessionLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public string LogPath { get; }

    public SessionLogger(ShareLayout layout)
    {
        Directory.CreateDirectory(layout.LogsDir);
        var fileName = $"{Environment.MachineName}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        LogPath = Path.Combine(layout.LogsDir, fileName);
        _writer = new StreamWriter(LogPath, append: true) { AutoFlush = true };
    }

    public void Write(SessionLogEntry entry)
    {
        var line = $"[{entry.Timestamp:HH:mm:ss}] {entry.Kind,-9} | {entry.Name,-40} | {entry.Status}"
                   + (entry.Detail is null ? "" : $" | {entry.Detail}");
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose() => _writer.Dispose();
}
