namespace DeployTool.Core.Models;

public enum ItemStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,

    /// <summary>MSI ProductCode was already installed — skipped, not run, not a failure.</summary>
    AlreadyInstalled
}
