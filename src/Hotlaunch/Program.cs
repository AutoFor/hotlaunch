using System.IO;
using System.Windows;
using Serilog;
using Serilog.Events;

namespace Hotlaunch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // --verbose / -v で全キー押下ログを出す。デフォルトは INF のみ。
        bool verbose = args.Contains("--verbose") || args.Contains("-v");
        // --tail でリアルタイムログウィンドウを表示する。
        bool tail = args.Contains("--tail");
        var minLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".hotlaunch", "hotlaunch.log");

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .WriteTo.File(logPath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7);

        // --tail: ログウィンドウを先に生成してシンクに追加
        LogWindow? logWindow = null;
        if (tail)
        {
            logWindow = new LogWindow();
            logConfig = logConfig.WriteTo.Sink(new LogWindowSink(logWindow));
        }

        Log.Logger = logConfig.CreateLogger();

        if (verbose)
            Log.Information("詳細ログモード (--verbose)");
        if (tail)
            Log.Information("ログウィンドウモード (--tail)");

        try
        {
            Log.Information("hotlaunch 起動");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            logWindow?.Show();
            using var trayApp = new TrayApp();
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "予期しないエラーで終了");
        }
        finally
        {
            Log.Information("hotlaunch 終了");
            Log.CloseAndFlush();
        }
    }
}
