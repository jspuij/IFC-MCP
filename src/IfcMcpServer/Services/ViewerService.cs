using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace IfcMcpServer.Services;

public class ViewerService : IDisposable
{
    private readonly ModelSession _session;
    private WebApplication? _app;
    private int _port;
    private readonly List<WebSocket> _clients = [];
    private readonly Lock _clientsLock = new();

    public bool IsRunning => _app != null;
    public string? Url => IsRunning ? $"http://localhost:{_port}" : null;

    public ViewerService(ModelSession session)
    {
        _session = session;
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        _port = GetAvailablePort();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenLocalhost(_port);
        });
        // Suppress all Kestrel output — MCP uses stdio
        builder.Logging.ClearProviders();

        _app = builder.Build();

        // Serve embedded viewer files
        var assembly = Assembly.GetExecutingAssembly();
        var embeddedProvider = new ManifestEmbeddedFileProvider(assembly, "Viewer");

        _app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = embeddedProvider
        });
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = embeddedProvider
        });

        // Serve the IFC model file
        _app.MapGet("/model.ifc", async context =>
        {
            var filePath = _session.FilePath;
            if (filePath == null || !File.Exists(filePath))
            {
                context.Response.StatusCode = 404;
                return;
            }
            context.Response.ContentType = "application/octet-stream";
            await context.Response.SendFileAsync(filePath);
        });

        _app.UseWebSockets();
        _app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }
            var ws = await context.WebSockets.AcceptWebSocketAsync();
            lock (_clientsLock) { _clients.Add(ws); }
            try
            {
                var buf = new byte[256];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buf, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            finally
            {
                lock (_clientsLock) { _clients.Remove(ws); }
            }
        });

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    public Task SendHighlightAsync(IReadOnlyList<string> globalIds) =>
        BroadcastAsync(new { action = "highlight", globalIds });

    public Task SendIsolateAsync(IReadOnlyList<string> globalIds) =>
        BroadcastAsync(new { action = "isolate", globalIds });

    public Task SendResetAsync() =>
        BroadcastAsync(new { action = "reset" });

    public Task SendCameraFitAsync(IReadOnlyList<string> globalIds) =>
        BroadcastAsync(new { action = "camera-fit", globalIds });

    public Task SendReloadAsync() =>
        BroadcastAsync(new { action = "reload" });

    private async Task BroadcastAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var bytes = Encoding.UTF8.GetBytes(json);

        WebSocket[] snapshot;
        lock (_clientsLock) { snapshot = [.. _clients]; }

        foreach (var ws in snapshot)
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
