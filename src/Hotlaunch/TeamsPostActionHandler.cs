using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Hotlaunch.Core;
using Serilog;

namespace Hotlaunch;

/// <summary>
/// Teams Third-Party Local API (WebSocket) 経由でミュートを操作する。
/// フォーカス移動なし、ショートカット注入なし。
/// 初回接続時は Teams の「許可」ダイアログが表示され、トークンを保存する。
/// </summary>
public class TeamsPostActionHandler : IPostActionHandler
{
    private static readonly string TokenFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".hotlaunch", "teams_token.txt");

    public bool CanHandle(string actionName) =>
        actionName.Equals("teams-mute", StringComparison.OrdinalIgnoreCase) ||
        actionName.Equals("teams-leave", StringComparison.OrdinalIgnoreCase);

    public void Execute(string actionName, bool isNewlyLaunched)
    {
        Task.Run(async () =>
        {
            try
            {
                await SendCommandAsync(actionName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Teams Local API エラー");
            }
        });
    }

    private static async Task SendCommandAsync(string actionName)
    {
        var token = LoadToken();
        using var ws = new ClientWebSocket();

        var query = $"manufacturer=hotlaunch&device=PC&app=hotlaunch&app-version=1.0&protocol-version=2.0.0&token={Uri.EscapeDataString(token ?? "")}";
        var uri = new Uri($"ws://127.0.0.1:8124/?{query}");

        await ws.ConnectAsync(uri, CancellationToken.None);
        Log.Information("Teams Local API 接続完了 (token={HasToken})", token != null ? "あり" : "なし");

        // 接続直後にコマンド送信
        var action = actionName.Equals("teams-mute", StringComparison.OrdinalIgnoreCase)
            ? "toggle-mute"
            : "leave-call";
        var payload = JsonSerializer.Serialize(new { action, parameters = new { }, requestId = 1 });
        await ws.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);
        Log.Information("Teams API コマンド送信: {Payload}", payload);

        // tokenRefresh を待機（初回: 30秒、通常: 3秒）
        var timeout = token == null ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(3);
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Log.Debug("Teams API 受信: {Msg}", msg);

                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("tokenRefresh", out var tokenProp))
                {
                    var newToken = tokenProp.GetString();
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        SaveToken(newToken);
                        Log.Information("Teams API トークン保存完了");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // タイムアウト（通常の接続では正常終了）
        }

        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }

    private static string? LoadToken()
    {
        try { return File.Exists(TokenFile) ? File.ReadAllText(TokenFile).Trim() : null; }
        catch { return null; }
    }

    private static void SaveToken(string token)
    {
        try { File.WriteAllText(TokenFile, token); }
        catch (Exception ex) { Log.Warning(ex, "トークン保存失敗"); }
    }
}
