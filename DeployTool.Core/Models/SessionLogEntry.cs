namespace DeployTool.Core.Models;

public sealed record SessionLogEntry(DateTimeOffset Timestamp, SessionItemKind Kind, string Name, ItemStatus Status, string? Detail);
