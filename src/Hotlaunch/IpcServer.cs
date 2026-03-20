using System.IO;
using System.IO.Pipes;
using Serilog;

namespace Hotlaunch;

/// <summary>
/// 名前付きパイプ経由で他プロセスからのコマンドを受け付けるサーバー。
/// </summary>
sealed class IpcServer : IDisposable
{
    public const string PipeName = "hotlaunch-ipc";

    public event Action<string>? CommandReceived;

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _task;

    public IpcServer()
    {
        _task = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_cts.Token);

                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(_cts.Token);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Log.Information("IPC コマンド受信: {Command}", line);
                    CommandReceived?.Invoke(line.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "IPC サーバーエラー");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
