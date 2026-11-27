using System.Text.Json.Serialization;

namespace Mountly.Core.Mounting;

/// <summary>
/// What the CLI reports with --json or --result-file. The app runs the CLI elevated for operations that
/// need administrator rights and reads this back.
/// </summary>
public sealed record CliResult
{
    public bool Ok { get; init; }
    public MountError? Error { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<MountedImage> Images { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliResult))]
public sealed partial class CliJson : JsonSerializerContext;
