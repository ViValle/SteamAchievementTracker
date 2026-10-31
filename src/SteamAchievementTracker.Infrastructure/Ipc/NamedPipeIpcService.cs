using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;

namespace SteamAchievementTracker.Infrastructure.Ipc;

public class NamedPipeIpcService : IIpcService
{
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    public event Action<NotificationPayload>? OnNotificationReceived;

    public Task StartServerAsync(string pipeName, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    while (!reader.EndOfStream && !token.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            try
                            {
                                var payload = JsonSerializer.Deserialize<NotificationPayload>(line);
                                if (payload != null)
                                {
                                    OnNotificationReceived?.Invoke(payload);
                                }
                            }
                            catch
                            {
                                // Ошибка десериализации пакета
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token);
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }, token);

        return Task.CompletedTask;
    }

    public async Task SendNotificationAsync(string pipeName, NotificationPayload payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            await client.ConnectAsync(1000);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            var json = JsonSerializer.Serialize(payload);
            await writer.WriteLineAsync(json);
        }
        catch
        {
            // Ошибка передачи по пайпу
        }
    }

    public void StopServer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopServer();
        GC.SuppressFinalize(this);
    }
}
