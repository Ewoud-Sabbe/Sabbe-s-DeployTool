using System.Runtime.InteropServices;

namespace DeployTool.Core.Services;

/// <summary>Authenticates the UNC share with the fixed "klant" account (no password) before anything reads from it.</summary>
public sealed class FileServerConnector
{
    private const int ResourcetypeDisk = 1;
    private const int NoError = 0;
    private const int ErrorAlreadyAssigned = 85;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource lpNetResource, string? lpPassword, string? lpUsername, int dwFlags);

    /// <summary>Connects to a UNC share as "klant" with no password. No-op if the path is already reachable (e.g. local test share).</summary>
    public void Connect(string uncPath)
    {
        if (Directory.Exists(uncPath)) return;

        var resource = new NetResource
        {
            dwType = ResourcetypeDisk,
            lpRemoteName = uncPath,
        };

        var result = WNetAddConnection2(ref resource, "", "klant", 0);
        if (result != NoError && result != ErrorAlreadyAssigned)
        {
            throw new IOException($"Kan geen verbinding maken met fileserver '{uncPath}' (foutcode {result}).");
        }
    }
}
