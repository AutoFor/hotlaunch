using System.IO;
using System.IO.Pipes;

namespace Hotlaunch;

/// <summary>
/// 常駐している hotlaunch プロセスへコマンドを送信するクライアント。
/// </summary>
static class IpcClient
{
    public static bool TrySend(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcServer.PipeName, PipeDirection.Out);
            pipe.Connect(1000);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(command);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
