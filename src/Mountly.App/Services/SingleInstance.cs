using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mountly.App.Services;

/// <summary>
/// One Mountly per user session. The first instance owns a named pipe; later starts send their
/// <see cref="StartupRequest"/> through it and exit.
/// </summary>
public static class SingleInstance
{
    private static readonly string PipeName = "Mountly-" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];

    private static Mutex? _mutex;

    /// <summary>True when another instance is running and took the request; this process should exit.</summary>
    public static bool TryForward(StartupRequest request)
    {
        _mutex = new Mutex(true, "Local\\" + PipeName, out var createdNew);
        if (createdNew) return false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, StartupJson.Default.StartupRequest);
            pipe.Write(payload);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            // The other instance is shutting down or hung; start normally rather than doing nothing.
            return false;
        }
    }

    /// <summary>Receives requests from later starts until disposed.</summary>
    public static IDisposable Listen(Action<StartupRequest> received)
    {
        var cancel = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancel.Token).ConfigureAwait(false);
                    using var buffer = new MemoryStream();
                    await server.CopyToAsync(buffer, cancel.Token).ConfigureAwait(false);
                    if (JsonSerializer.Deserialize(buffer.ToArray(), StartupJson.Default.StartupRequest) is { } request) received(request);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    await Task.Delay(200).ConfigureAwait(false); // a broken client; keep listening
                }
            }
        });
        return new Stopper(cancel);
    }

    private sealed class Stopper(CancellationTokenSource cancel) : IDisposable
    {
        public void Dispose()
        {
            cancel.Cancel();
            _mutex?.Dispose();
        }
    }
}
