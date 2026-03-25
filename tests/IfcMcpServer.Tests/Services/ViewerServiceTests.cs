using IfcMcpServer.Services;

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

    public void Dispose()
    {
        _viewer.StopAsync().GetAwaiter().GetResult();
        _session.Dispose();
    }
}
