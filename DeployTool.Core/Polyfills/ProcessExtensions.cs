#if NETFRAMEWORK
using System.Diagnostics;

namespace DeployTool.Core.Polyfills;

/// <summary>Process.WaitForExitAsync was added in .NET 5 — not available on .NET Framework.</summary>
internal static class ProcessExtensions
{
    public static Task WaitForExitAsync(this Process process, CancellationToken ct = default)
    {
        if (process.HasExited) return Task.CompletedTask;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult(null);

        if (process.HasExited) tcs.TrySetResult(null);

        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }
}
#endif
