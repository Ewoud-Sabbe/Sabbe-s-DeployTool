using DeployTool.Core.Models;

namespace DeployTool.Core.Services;

/// <summary>Writes each event immediately (not just at the end) to Logs\{computer}_{timestamp}.log on the share.</summary>
public sealed class SessionLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public string LogPath { get; }

    /// <summary>Raised for every line written, so the UI can mirror the log file live.</summary>
    public event Action<string>? LineWritten;

    public SessionLogger(ShareLayout layout)
    {
        Directory.CreateDirectory(layout.LogsDir);
        var fileName = $"{Environment.MachineName}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        LogPath = Path.Combine(layout.LogsDir, fileName);
        _writer = new StreamWriter(LogPath, append: true) { AutoFlush = true };
    }

    /// <summary>Logs a status transition (wachtend/bezig/geslaagd/mislukt) for an item.</summary>
    public void Write(SessionLogEntry entry)
    {
        var line = $"[{entry.Timestamp:HH:mm:ss.fff}] {entry.Kind,-9} | {entry.Name,-40} | {entry.Status}"
                   + (entry.Detail is null ? "" : $" | {entry.Detail}");
        WriteRaw(line);
    }

    /// <summary>Logs a freeform, timestamped detail line (copy progress, process args, exit codes, ...).</summary>
    public void WriteLine(string message) => WriteRaw($"[{DateTimeOffset.Now:HH:mm:ss.fff}]   {message}");

    private void WriteRaw(string line)
    {
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
        LineWritten?.Invoke(line);
    }

    public void Dispose() => _writer.Dispose();
}
