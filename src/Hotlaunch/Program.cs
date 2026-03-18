using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Serilog;
using Serilog.Events;

namespace Hotlaunch;

static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [STAThread]
    static void Main(string[] args)
    {
        AllocConsole();
        // コンソールウィンドウにフォーカスがある状態で Ctrl+C が届いても
        // hotlaunch が終了しないようにする（モディファイアリマップの Ctrl 注入対策）
        Console.CancelKeyPress += (_, e) => e.Cancel = true;

        // --verbose / -v で全キー押下ログを出す。デフォルトは INF のみ。
        bool verbose = args.Contains("--verbose") || args.Contains("-v");
        var minLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".hotlaunch", "hotlaunch.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        if (verbose)
            Log.Information("詳細ログモード (--verbose)");

        try
        {
            Log.Information("hotlaunch 起動");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
