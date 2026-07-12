namespace DeployTool.Core.Models;

public sealed record SessionItemProgress(SessionItem Item, ItemStatus Status, string? ErrorMessage = null);
