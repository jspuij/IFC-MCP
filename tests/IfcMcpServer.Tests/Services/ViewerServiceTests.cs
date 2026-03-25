using IfcMcpServer.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace IfcMcpServer.Tests.Services;

public class ViewerServiceTests : IDisposable
{
    private readonly ModelSession _session = new();
    private readonly ViewerService _viewer;

    public ViewerServiceTests()
    {
        TestModelBuilder.EnsureTestModel();
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _viewer = new ViewerService(_session);
    }

    [Fact]
    public void InitialState_NotRunning()
    {
        Assert.False(_viewer.IsRunning);
        Assert.Null(_viewer.Url);
    }

    [Fact]
    public async Task StartAsync_StartsServer()
    {
        await _viewer.StartAsync();
        Assert.True(_viewer.IsRunning);
        Assert.NotNull(_viewer.Url);
        Assert.Contains("http://localhost:", _viewer.Url);
    }

    [Fact]
    public async Task StartAsync_ServesIndexHtml()
    {
        await _viewer.StartAsync();
        using var http = new HttpClient();
        var response = await http.GetAsync(_viewer.Url);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("IFC Viewer", content);
    }

    [Fact]
    public async Task StartAsync_ServesModelIfc()
    {
        await _viewer.StartAsync();
        using var http = new HttpClient();
        var response = await http.GetAsync($"{_viewer.Url}/model.ifc");
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Content.Headers.ContentLength > 0);
    }

    [Fact]
    public async Task StopAsync_StopsServer()
    {
        await _viewer.StartAsync();
        await _viewer.StopAsync();
        Assert.False(_viewer.IsRunning);
        Assert.Null(_viewer.Url);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_IsIdempotent()
    {
        await _viewer.StartAsync();
        var firstUrl = _viewer.Url;
        await _viewer.StartAsync();
        Assert.Equal(firstUrl, _viewer.Url);
    }

    [Fact]
    public async Task WebSocket_AcceptsConnection()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task SendHighlightAsync_SendsJsonToClient()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        await Task.Delay(50);
        await _viewer.SendHighlightAsync(["abc123", "def456"]);
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("highlight", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("globalIds").GetArrayLength());
    }

    [Fact]
    public async Task SendIsolateAsync_SendsJsonToClient()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        await Task.Delay(50);
        await _viewer.SendIsolateAsync(["abc123"]);
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("isolate", doc.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public async Task SendResetAsync_SendsJsonToClient()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        await Task.Delay(50);
        await _viewer.SendResetAsync();
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("reset", doc.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public async Task SendCameraFitAsync_SendsJsonToClient()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        await Task.Delay(50);
        await _viewer.SendCameraFitAsync(["abc123"]);
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("camera-fit", doc.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public async Task SendReloadAsync_SendsJsonToClient()
    {
        await _viewer.StartAsync();
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(_viewer.Url!.Replace("http://", "ws://") + "/ws"), CancellationToken.None);
        await Task.Delay(50);
        await _viewer.SendReloadAsync();
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("reload", doc.RootElement.GetProperty("action").GetString());
    }

    public void Dispose()
    {
        _viewer.StopAsync().GetAwaiter().GetResult();
        _session.Dispose();
    }
}
