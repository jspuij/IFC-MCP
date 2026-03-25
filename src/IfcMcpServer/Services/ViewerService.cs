using System.Reflection;
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

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app == null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
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
