#if NETFRAMEWORK
using System.Diagnostics;

namespace DeployTool.Core.Polyfills;

/// <summary>Process.WaitForExitAsync was added in .NET 5 — not available on .NET Framework.</summary>
internal static class ProcessExtensions
{
    public static async Task WaitForExitAsync(this Process process, CancellationToken ct = default)
    {
        if (process.HasExited) return;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onExited = (_, _) => tcs.TrySetResult(null);

        process.EnableRaisingEvents = true;
        process.Exited += onExited;
        try
        {
            if (process.HasExited)
                return;

            // Dispose the registration when done — one long-lived session token is used for many
            // process waits, and undisposed registrations would pile up on it.
            using (ct.Register(() => tcs.TrySetCanceled(ct)))
                await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            process.Exited -= onExited;
        }
    }
}
#endif
