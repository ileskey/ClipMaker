namespace ClipMaker.Media.Infrastructure.Tools;

public sealed record ToolSearchRequest(string ExecutableName, string? UserSpecifiedPath = null);
