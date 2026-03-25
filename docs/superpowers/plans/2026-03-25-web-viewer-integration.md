# Web Viewer Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a browser-based 3D IFC viewer to the MCP server, controllable via MCP tools over WebSocket (highlight, isolate, reset, camera-fit).

**Architecture:** The .NET MCP server gains an embedded Kestrel web server (ViewerService) that serves static viewer files and a raw `.ifc` endpoint, plus a WebSocket endpoint for pushing commands. The browser-side viewer uses That Open Engine (@thatopen/components + web-ifc + Three.js) to parse and render the IFC model, and listens for WebSocket commands to highlight/isolate/reset elements.

**Tech Stack:** .NET 10 (Kestrel), WebSocket, @thatopen/components 3.x, @thatopen/fragments, web-ifc, Three.js

**Spec:** `docs/superpowers/specs/2026-03-25-web-viewer-integration-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `src/IfcMcpServer/Services/ViewerService.cs` | Kestrel web server lifecycle, WebSocket management, command dispatch |
| `src/IfcMcpServer/Tools/ViewerTools.cs` | MCP tool wrappers for viewer-open/close/highlight/isolate/reset/camera |
| `src/IfcMcpServer/Viewer/index.html` | Single-page viewer app |
| `src/IfcMcpServer/Viewer/viewer.js` | IFC loading, Three.js scene, WebSocket client, highlight/isolate logic |
| `src/IfcMcpServer/Viewer/style.css` | Minimal full-viewport styling |
| `tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs` | Tests for ViewerService lifecycle and WebSocket messaging |

### Modified Files
| File | Change |
|------|--------|
| `src/IfcMcpServer/IfcMcpServer.csproj` | Switch SDK to `Microsoft.NET.Sdk.Web`, add embedded resource glob for `Viewer/**` |
| `src/IfcMcpServer/Program.cs` | Register `ViewerService` singleton |
| `src/IfcMcpServer/Tools/ModelTools.cs` | Inject `ViewerService`, call reload on open-model, stop on close-model |

---

## Task 1: Switch to Web SDK and Configure Embedded Resources

**Files:**
- Modify: `src/IfcMcpServer/IfcMcpServer.csproj`

- [ ] **Step 1: Update .csproj SDK and add embedded resources**

Change the SDK from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` and add the embedded resource glob:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateEmbeddedFilesManifest>true</GenerateEmbeddedFilesManifest>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ClosedXML" Version="0.105.0" />
    <PackageReference Include="Microsoft.Extensions.FileProviders.Embedded" Version="10.0.5" />
    <PackageReference Include="ModelContextProtocol" Version="1.1.0" />
    <PackageReference Include="Xbim.Essentials" Version="6.0.578" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Viewer/**/*" />
  </ItemGroup>

</Project>
```

Note: `Microsoft.Extensions.Hosting` is removed since the Web SDK provides it implicitly. `Microsoft.Extensions.FileProviders.Embedded` is added for `ManifestEmbeddedFileProvider`. The glob uses forward slashes for cross-platform compatibility.

- [ ] **Step 2: Create placeholder viewer file**

Create `src/IfcMcpServer/Viewer/index.html` with minimal content so the embedded resource glob has something to include:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>IFC Viewer</title>
</head>
<body>
  <p>Viewer placeholder</p>
</body>
</html>
```

- [ ] **Step 3: Verify build still passes**

Run: `dotnet build`
Expected: Build succeeds. The SDK change should be transparent since we're not adding any ASP.NET middleware to the MCP host yet.

- [ ] **Step 4: Run existing tests to verify no regression**

Run: `dotnet test`
Expected: All existing tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/IfcMcpServer/IfcMcpServer.csproj src/IfcMcpServer/Viewer/index.html
git commit -m "chore: switch to Web SDK, add Viewer embedded resource glob"
```

---

## Task 2: Implement ViewerService — Web Server Lifecycle

**Files:**
- Create: `src/IfcMcpServer/Services/ViewerService.cs`
- Create: `tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs`
- Modify: `src/IfcMcpServer/Program.cs`

This task implements the Kestrel web server that serves static files and the IFC model file. WebSocket support is added in Task 3.

- [ ] **Step 1: Write failing tests for ViewerService lifecycle**

Create `tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ViewerServiceTests"`
Expected: FAIL — `ViewerService` class does not exist.

- [ ] **Step 3: Implement ViewerService**

Create `src/IfcMcpServer/Services/ViewerService.cs`:

```csharp
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
```

**Note:** The `.csproj` already includes `GenerateEmbeddedFilesManifest` and the `Microsoft.Extensions.FileProviders.Embedded` package from Task 1.

- [ ] **Step 4: Register ViewerService in DI**

In `src/IfcMcpServer/Program.cs`, add after the other singleton registrations (line 16):

```csharp
builder.Services.AddSingleton<ViewerService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "ViewerServiceTests"`
Expected: All 6 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/IfcMcpServer/Services/ViewerService.cs tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs src/IfcMcpServer/Program.cs src/IfcMcpServer/IfcMcpServer.csproj
git commit -m "feat: add ViewerService with Kestrel web server for static files and IFC serving"
```

---

## Task 3: Add WebSocket Support to ViewerService

**Files:**
- Modify: `src/IfcMcpServer/Services/ViewerService.cs`
- Modify: `tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs`

- [ ] **Step 1: Write failing tests for WebSocket messaging**

Add to `ViewerServiceTests.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

// ... add these test methods to the existing class:

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

    await _viewer.SendReloadAsync();

    var buffer = new byte[4096];
    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
    using var doc = JsonDocument.Parse(json);

    Assert.Equal("reload", doc.RootElement.GetProperty("action").GetString());
}
```

**Note on WebSocket test race condition:** There is a small window between `ConnectAsync` completing on the client side and the server-side handler adding the client to `_clients`. If tests are flaky, add a short `await Task.Delay(50)` after `ConnectAsync` before calling Send methods.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "ViewerServiceTests"`
Expected: FAIL — `SendHighlightAsync` etc. do not exist.

- [ ] **Step 3: Add WebSocket support to ViewerService**

Add the following fields and methods to `ViewerService`:

```csharp
// New field
private readonly List<WebSocket> _clients = [];
private readonly Lock _clientsLock = new();

// In StartAsync(), before await _app.StartAsync(), add:
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
        // Keep connection alive — read until closed
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

// New public methods:
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
```

Add required usings at top:
```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "ViewerServiceTests"`
Expected: All 12 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/IfcMcpServer/Services/ViewerService.cs tests/IfcMcpServer.Tests/Services/ViewerServiceTests.cs
git commit -m "feat: add WebSocket support to ViewerService for real-time viewer commands"
```

---

## Task 4: Implement ViewerTools (MCP Tools)

**Files:**
- Create: `src/IfcMcpServer/Tools/ViewerTools.cs`

- [ ] **Step 1: Create ViewerTools**

Create `src/IfcMcpServer/Tools/ViewerTools.cs`:

```csharp
using System.ComponentModel;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class ViewerTools
{
    [McpServerTool(Name = "viewer-open", ReadOnly = false),
     Description("Start the 3D web viewer and return the URL. Opens in a browser to visualize the currently loaded IFC model.")]
    public static async Task<string> ViewerOpen(
        ModelSession session,
        ViewerService viewer)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        await viewer.StartAsync();
        return $"Viewer started at {viewer.Url}\nOpen this URL in a browser to see the 3D model.";
    }

    [McpServerTool(Name = "viewer-close", ReadOnly = false),
     Description("Stop the 3D web viewer.")]
    public static async Task<string> ViewerClose(ViewerService viewer)
    {
        if (!viewer.IsRunning)
            return "Viewer is not running.";

        await viewer.StopAsync();
        return "Viewer stopped.";
    }

    [McpServerTool(Name = "viewer-highlight", ReadOnly = true),
     Description("Highlight specific elements in the 3D viewer by their GlobalId. Other elements are dimmed.")]
    public static async Task<string> ViewerHighlight(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to highlight")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendHighlightAsync(globalIds);
        return $"Highlighted {globalIds.Length} element(s) in the viewer.";
    }

    [McpServerTool(Name = "viewer-isolate", ReadOnly = true),
     Description("Isolate specific elements in the 3D viewer — hides everything except the specified elements.")]
    public static async Task<string> ViewerIsolate(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to isolate")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendIsolateAsync(globalIds);
        return $"Isolated {globalIds.Length} element(s) in the viewer.";
    }

    [McpServerTool(Name = "viewer-reset", ReadOnly = true),
     Description("Reset the 3D viewer to show all elements with default visibility and appearance.")]
    public static async Task<string> ViewerReset(ViewerService viewer)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendResetAsync();
        return "Viewer reset to default state.";
    }

    [McpServerTool(Name = "viewer-camera", ReadOnly = true),
     Description("Fly the camera to fit specific elements in the 3D viewer.")]
    public static async Task<string> ViewerCamera(
        ViewerService viewer,
        [Description("Array of IFC GlobalIds to fit in the camera view")] string[] globalIds)
    {
        if (!viewer.IsRunning)
            return "Error: Viewer is not running. Use viewer-open first.";

        await viewer.SendCameraFitAsync(globalIds);
        return $"Camera fitted to {globalIds.Length} element(s).";
    }
}
```

- [ ] **Step 2: Build to verify tool discovery works**

Run: `dotnet build`
Expected: Build succeeds. The `[McpServerToolType]` and `[McpServerTool]` attributes will be picked up by `WithToolsFromAssembly()`.

- [ ] **Step 3: Run all tests to verify no regression**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/IfcMcpServer/Tools/ViewerTools.cs
git commit -m "feat: add ViewerTools MCP tools for viewer-open/close/highlight/isolate/reset/camera"
```

---

## Task 5: Integrate ViewerService with ModelTools

**Files:**
- Modify: `src/IfcMcpServer/Tools/ModelTools.cs`

- [ ] **Step 1: Modify OpenModel to trigger viewer reload**

Update the `OpenModel` method signature to accept `ViewerService` and send reload if running. Change `ModelTools.cs`:

```csharp
[McpServerTool(Name = "open-model", ReadOnly = false), Description("Open an IFC file for querying. Closes any previously opened model.")]
public static async Task<string> OpenModel(
    ModelSession session,
    ViewerService viewer,
    [Description("Absolute or relative path to the IFC file")] string filePath)
{
    session.OpenModel(filePath);
    var model = session.CurrentModel!;

    var schemaVersion = model.SchemaVersion.ToString();
    var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
    var projectName = project?.Name?.ToString() ?? "(unnamed)";
    var totalElements = model.Instances.OfType<IIfcProduct>().Count();

    if (viewer.IsRunning)
        await viewer.SendReloadAsync();

    return $"Model opened: {filePath}\nSchema: {schemaVersion}\nProject: {projectName}\nTotal elements: {totalElements}";
}
```

- [ ] **Step 2: Modify CloseModel to stop viewer**

Update the `CloseModel` method:

```csharp
[McpServerTool(Name = "close-model", ReadOnly = false), Description("Close the currently loaded IFC model and free memory.")]
public static async Task<string> CloseModel(
    ModelSession session,
    ViewerService viewer)
{
    if (!session.IsModelLoaded)
        return "No model is currently loaded.";

    if (viewer.IsRunning)
        await viewer.StopAsync();

    session.CloseModel();
    return "Model closed.";
}
```

Note: The `ModelInfo` method does not need changes — it has no viewer interaction.

- [ ] **Step 3: Run all tests**

Run: `dotnet test`
Expected: All tests pass. Existing `ModelTools` tests don't call tools directly (they test services), so the signature change doesn't break them.

- [ ] **Step 4: Commit**

```bash
git add src/IfcMcpServer/Tools/ModelTools.cs
git commit -m "feat: integrate ViewerService with open-model and close-model tools"
```

---

## Task 6: Build the Browser Viewer — HTML Shell and Styling

**Files:**
- Modify: `src/IfcMcpServer/Viewer/index.html`
- Create: `src/IfcMcpServer/Viewer/style.css`

- [ ] **Step 1: Write the HTML page**

Replace the placeholder `src/IfcMcpServer/Viewer/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>IFC Viewer</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <div id="container"></div>
  <div id="status">Loading model...</div>
  <script type="importmap">
  {
    "imports": {
      "three": "https://unpkg.com/three@0.173.0/build/three.module.js",
      "three/examples/jsm/": "https://unpkg.com/three@0.173.0/examples/jsm/",
      "web-ifc": "https://unpkg.com/web-ifc@0.0.75/web-ifc-api.js",
      "@thatopen/components": "https://unpkg.com/@thatopen/components/dist/index.min.js",
      "@thatopen/fragments": "https://unpkg.com/@thatopen/fragments/dist/index.min.js"
    }
  }
  </script>
  <script type="module" src="viewer.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write the CSS**

Create `src/IfcMcpServer/Viewer/style.css`:

```css
* { margin: 0; padding: 0; box-sizing: border-box; }

html, body {
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #1a1a2e;
}

#container {
  width: 100%;
  height: 100%;
}

#status {
  position: fixed;
  bottom: 16px;
  left: 16px;
  color: #94a3b8;
  font-family: system-ui, sans-serif;
  font-size: 13px;
  background: rgba(0, 0, 0, 0.6);
  padding: 6px 12px;
  border-radius: 4px;
  pointer-events: none;
  z-index: 10;
}
```

- [ ] **Step 3: Commit**

```bash
git add src/IfcMcpServer/Viewer/index.html src/IfcMcpServer/Viewer/style.css
git commit -m "feat: add viewer HTML shell and CSS"
```

---

## Task 7: Build the Browser Viewer — IFC Loading and 3D Scene

**Files:**
- Create: `src/IfcMcpServer/Viewer/viewer.js`

This is the core viewer logic. It initializes That Open Engine, loads the IFC model, sets up the WebSocket connection, and handles highlight/isolate/reset/camera commands.

- [ ] **Step 1: Write viewer.js**

Create `src/IfcMcpServer/Viewer/viewer.js`:

```javascript
import * as THREE from "three";
import * as OBC from "@thatopen/components";
import * as FRAGS from "@thatopen/fragments";

// ── State ──
let components;
let world;
let fragments;
let ifcLoader;
let hider;

const status = document.getElementById("status");
const container = document.getElementById("container");

// ── Scene Setup ──
async function initScene() {
  components = new OBC.Components();
  const worlds = components.get(OBC.Worlds);
  world = worlds.create();

  world.scene = new OBC.SimpleScene(components);
  world.scene.setup();
  world.scene.three.background = null;

  world.renderer = new OBC.SimpleRenderer(components, container);
  world.camera = new OBC.OrthoPerspectiveCamera(components);

  components.init();
  components.get(OBC.Grids).create(world);

  // IFC loader
  ifcLoader = components.get(OBC.IfcLoader);
  await ifcLoader.setup({
    autoSetWasm: false,
    wasm: {
      path: "https://unpkg.com/web-ifc@0.0.75/",
      absolute: true,
    },
  });

  // Fragments
  fragments = components.get(OBC.FragmentsManager);
  const workerUrl = "https://thatopen.github.io/engine_fragment/resources/worker.mjs";
  const fetched = await fetch(workerUrl);
  const blob = await fetched.blob();
  const file = new File([blob], "worker.mjs", { type: "text/javascript" });
  const url = URL.createObjectURL(file);
  fragments.init(url);

  world.camera.controls.addEventListener("update", () => fragments.core.update());
  fragments.list.onItemSet.add(({ value: model }) => {
    model.useCamera(world.camera.three);
    world.scene.three.add(model.object);
    fragments.core.update(true);
  });

  // Hider for isolate/reset
  hider = components.get(OBC.Hider);
}

// ── Load IFC Model ──
async function loadModel() {
  status.textContent = "Loading model...";
  try {
    const response = await fetch("/model.ifc");
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const data = await response.arrayBuffer();
    const buffer = new Uint8Array(data);
    await ifcLoader.load(buffer, false, "model");
    status.textContent = "Model loaded.";
  } catch (err) {
    status.textContent = `Error loading model: ${err.message}`;
    console.error(err);
  }
}

// ── WebSocket ──
function connectWebSocket() {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const ws = new WebSocket(`${protocol}//${location.host}/ws`);

  ws.onopen = () => console.log("WebSocket connected");
  ws.onclose = () => {
    console.log("WebSocket disconnected, reconnecting in 2s...");
    setTimeout(connectWebSocket, 2000);
  };
  ws.onerror = (err) => console.error("WebSocket error:", err);

  ws.onmessage = (event) => {
    try {
      const msg = JSON.parse(event.data);
      handleCommand(msg);
    } catch (err) {
      console.error("Invalid WebSocket message:", err);
    }
  };
}

// ── Command Handlers ──
function handleCommand(msg) {
  switch (msg.action) {
    case "highlight":
      highlightElements(msg.globalIds);
      break;
    case "isolate":
      isolateElements(msg.globalIds);
      break;
    case "reset":
      resetView();
      break;
    case "camera-fit":
      fitCamera(msg.globalIds);
      break;
    case "reload":
      reloadModel();
      break;
    default:
      console.warn("Unknown action:", msg.action);
  }
}

function findFragmentIdsByGlobalIds(globalIds) {
  // Build a map of globalId -> fragment item for selection
  const idSet = new Set(globalIds);
  const selectionMap = new Map();

  for (const [, model] of fragments.list) {
    // Iterate the model's items to find matching GlobalIds
    if (!model.object) continue;
    model.object.traverse((child) => {
      if (!child.isMesh) return;
      const frag = child;
      if (!frag.fragment) return;
      for (const [itemId, expressIds] of frag.fragment.ids) {
        // itemId is the GlobalId in fragments
        if (idSet.has(itemId)) {
          if (!selectionMap.has(frag.fragment.id)) {
            selectionMap.set(frag.fragment.id, new Set());
          }
          for (const eid of expressIds) {
            selectionMap.get(frag.fragment.id).add(eid);
          }
        }
      }
    });
  }
  return selectionMap;
}

function highlightElements(globalIds) {
  // Reset first, then dim everything except the highlighted elements.
  // This uses the same isolate mechanism as a fallback — a proper color
  // overlay (e.g., tinted material) can replace this once the fragment
  // color API is confirmed at runtime.
  resetView();
  if (!globalIds || globalIds.length === 0) return;

  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  if (selectionMap.size > 0) {
    // Hide all, then show only selected — functionally identical to isolate
    // for v1. A future iteration can use material overrides to dim rather
    // than fully hide non-selected elements.
    hider.set(false);
    hider.set(true, selectionMap);
  }
  status.textContent = `Highlighted ${globalIds.length} element(s)`;
}

function isolateElements(globalIds) {
  if (!globalIds || globalIds.length === 0) return;
  // Hide everything, then show only the specified elements
  hider.set(false); // Hide all
  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  hider.set(true, selectionMap); // Show only selected
  status.textContent = `Isolated ${globalIds.length} element(s)`;
}

function resetView() {
  hider.set(true); // Show all
  status.textContent = "View reset.";
}

async function fitCamera(globalIds) {
  if (!globalIds || globalIds.length === 0) return;
  // Compute bounding box from fragments matching the globalIds
  const selectionMap = findFragmentIdsByGlobalIds(globalIds);
  const box = new THREE.Box3();
  for (const [fragId, expressIds] of selectionMap) {
    // Find the mesh in the scene by traversing
    world.scene.three.traverse((child) => {
      if (!child.isMesh || !child.fragment) return;
      if (child.fragment.id === fragId) {
        // Get bounding box of the entire fragment mesh as approximation
        const meshBox = new THREE.Box3().setFromObject(child);
        box.union(meshBox);
      }
    });
  }
  if (!box.isEmpty()) {
    await world.camera.fit(box);
  }
  status.textContent = `Camera fitted to ${globalIds.length} element(s)`;
}

async function reloadModel() {
  status.textContent = "Reloading model...";
  // Dispose existing model
  try {
    await fragments.disposeModel("model");
  } catch { /* ignore if no model */ }
  await loadModel();
}

// ── Init ──
async function main() {
  await initScene();
  await loadModel();
  connectWebSocket();
}

main().catch(console.error);
```

**Important note for the implementer:** The That Open Engine API evolves. The fragment traversal logic in `findFragmentIdsByGlobalIds`, the `hider.set()` API, and the `world.camera.fit()` method may need adjustment based on the actual runtime behavior. The implementer should:
1. Load a real IFC model in the browser
2. Inspect `fragments.list` in the console to understand the actual data structure
3. Adjust the selection map construction and camera-fit call accordingly
4. Verify the CDN import map URLs resolve correctly — update versions if needed

The `highlightElements` function currently uses the same hide/show mechanism as isolate (hiding non-selected elements). A future iteration can use material overrides to dim rather than fully hide, giving a true "highlight with context" effect. The `fitCamera` function approximates by using the bounding box of entire fragment meshes; per-element bounding boxes may require a different API approach.

- [ ] **Step 2: Verify build includes viewer files**

Run: `dotnet build`
Then verify the embedded resources are included:
Run: `dotnet run --project src/IfcMcpServer -- --help 2>&1 || true`
(The server will fail to start cleanly in a terminal without MCP client, that's fine — we just need the build to succeed.)

- [ ] **Step 3: Commit**

```bash
git add src/IfcMcpServer/Viewer/viewer.js
git commit -m "feat: add browser-side IFC viewer with WebSocket command handling"
```

---

## Task 8: End-to-End Manual Test

This task is a manual integration test to verify the full flow works.

- [ ] **Step 1: Start the MCP server with a real IFC file**

Use the MCP inspector or Claude Desktop to:
1. Call `open-model` with a real `.ifc` file
2. Call `viewer-open` — note the returned URL
3. Open the URL in a browser
4. Verify the 3D model loads and renders

- [ ] **Step 2: Test highlight and isolate**

1. Call `list-elements` to get some GlobalIds
2. Call `viewer-highlight` with those GlobalIds — verify the browser responds
3. Call `viewer-isolate` with a subset — verify only those elements are visible
4. Call `viewer-reset` — verify all elements return to normal
5. Call `viewer-camera` — verify the camera moves

- [ ] **Step 3: Test model reload**

1. Call `open-model` with a different `.ifc` file while the viewer is open
2. Verify the browser reloads the new model automatically

- [ ] **Step 4: Test close**

1. Call `close-model` — verify the viewer stops
2. Call `viewer-open` — verify it returns an error (no model loaded)

- [ ] **Step 5: Fix any issues found during manual testing**

Address bugs, adjust the That Open Engine API calls as needed based on actual runtime behavior.

- [ ] **Step 6: Commit any fixes**

```bash
git add -A
git commit -m "fix: adjustments from end-to-end viewer testing"
```

---

## Summary

| Task | What | Key Files |
|------|------|-----------|
| 1 | Switch to Web SDK, configure embedded resources | `.csproj` |
| 2 | ViewerService — Kestrel web server lifecycle | `ViewerService.cs`, `ViewerServiceTests.cs`, `Program.cs` |
| 3 | Add WebSocket support to ViewerService | `ViewerService.cs`, `ViewerServiceTests.cs` |
| 4 | ViewerTools — MCP tool wrappers | `ViewerTools.cs` |
| 5 | Integrate ViewerService with ModelTools | `ModelTools.cs` |
| 6 | Browser viewer HTML + CSS shell | `index.html`, `style.css` |
| 7 | Browser viewer JS — IFC loading + WebSocket commands | `viewer.js` |
| 8 | End-to-end manual test | — |
