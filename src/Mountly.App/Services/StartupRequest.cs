using System.Text.Json.Serialization;
using Mountly.Core.Images;
using Mountly.Windows;

namespace Mountly.App.Services;

public enum StartupAction
{
    /// <summary>Show the window (or nothing, with --tray).</summary>
    Open,
    Mount,
    Convert,
    Verify,
    CreateIso,
}

/// <summary>What a start of Mountly asks for: from Explorer verbs, a double-clicked image or the Run key.</summary>
public sealed record StartupRequest
{
    public StartupAction Action { get; init; }
    public string? Path { get; init; }

    /// <summary>Started at sign-in: stay in the tray.</summary>
    public bool Tray { get; init; }

    public static StartupRequest Parse(IReadOnlyList<string> args)
    {
        var request = new StartupRequest();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            var next = i + 1 < args.Count ? args[i + 1] : null;
            switch (a)
            {
                case AppArguments.Tray:
                    request = request with { Tray = true };
                    break;
                case AppArguments.Mount when next is not null:
                    request = request with { Action = StartupAction.Mount, Path = next };
                    i++;
                    break;
                case AppArguments.Convert when next is not null:
                    request = request with { Action = StartupAction.Convert, Path = next };
                    i++;
                    break;
                case AppArguments.Verify when next is not null:
                    request = request with { Action = StartupAction.Verify, Path = next };
                    i++;
                    break;
                case AppArguments.CreateIso when next is not null:
                    request = request with { Action = StartupAction.CreateIso, Path = next };
                    i++;
                    break;
                default:
                    // "Open with Mountly" and drag-onto-exe pass the bare path.
                    if (!a.StartsWith('-') && ImageFormats.Detect(a) != ImageFormat.Unknown)
                        request = request with { Action = StartupAction.Mount, Path = a };
                    break;
            }
        }

        return request.Path is { } p ? request with { Path = System.IO.Path.GetFullPath(p) } : request;
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(StartupRequest))]
internal sealed partial class StartupJson : JsonSerializerContext;
