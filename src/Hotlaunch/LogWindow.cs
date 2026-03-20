using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Hotlaunch;

/// <summary>--tail 起動時に表示するリアルタイムログウィンドウ。</summary>
sealed class LogWindow : Window
{
    private readonly TextBox _textBox;

    public LogWindow()
    {
        Title = "hotlaunch ログ";
        Width = 800;
        Height = 400;
        Background = new SolidColorBrush(Color.FromRgb(20, 20, 20));

        _textBox = new TextBox
        {
            IsReadOnly = true,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            TextWrapping = TextWrapping.NoWrap,
        };

        Content = _textBox;
    }

    public void Append(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _textBox.AppendText(line);
            _textBox.ScrollToEnd();
        });
    }
}

/// <summary>Serilog のカスタムシンク。LogWindow にログを流す。</summary>
sealed class LogWindowSink : ILogEventSink
{
    private static readonly MessageTemplateTextFormatter Formatter =
        new("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
            null);

    private readonly LogWindow _window;

    public LogWindowSink(LogWindow window) => _window = window;

    public void Emit(LogEvent logEvent)
    {
        var writer = new System.IO.StringWriter();
        Formatter.Format(logEvent, writer);
        _window.Append(writer.ToString());
    }
}
