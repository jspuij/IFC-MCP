# Web Viewer Integration Design

Integrate a browser-based 3D IFC viewer into the MCP server, controlled via MCP tools over WebSocket.

## Goals

- View the currently loaded IFC model in a browser-based 3D viewer
- Highlight and isolate elements found via existing MCP query tools
- Real-time communication between MCP tools and the viewer via WebSocket

## Non-Goals (v1)

- Element list or property panels in the browser UI (future work)
- Browser-to-MCP interaction (e.g., clicking an element to query it)
- IFC file format conversion — the raw `.ifc` file is served as-is

## Architecture

```
MCP Client ◄──stdio/JSON-RPC──► IFC MCP Server (.NET) ◄──HTTP+WS──► Browser
                                 │                                    │
                                 ├─ ModelSession (existing)           ├─ Three.js scene
                                 ├─ QueryTools (existing)             ├─ web-ifc parser
                                 ├─ ViewerService (new)               ├─ WebSocket client
                                 │  ├─ Kestrel HTTP server            └─ Highlight/Isolate
                                 │  ├─ Static file serving                rendering
                                 │  ├─ IFC file endpoint
                                 │  └─ WebSocket endpoint
                                 └─ ViewerTools (new)
```

## Server-Side Components

### SDK & Hosting

The project currently uses `Microsoft.NET.Sdk`. To use Kestrel, switch to `Microsoft.NET.Sdk.Web` in the `.csproj`. This gives access to ASP.NET Core (Kestrel, WebSocket middleware) without additional package references.

**Stdio isolation:** The MCP server uses stdio for JSON-RPC. Kestrel must not write to stdout or it will corrupt the MCP transport. ViewerService runs Kestrel on a separate `WebApplication` instance with logging configured to write only to stderr (or suppressed entirely). The Kestrel host is independent from the MCP host — it is started/stopped on demand by ViewerService, not at process startup.

### ViewerService (singleton)

Manages the embedded web server lifecycle.

**Responsibilities:**
- Start a Kestrel HTTP server on a random available port (separate `WebApplication`, not the MCP host)
- Serve static viewer files (HTML/JS/CSS) from embedded resources bundled from `src/IfcMcpServer/Viewer/`
- Serve the raw `.ifc` file from `ModelSession.FilePath` at `/model.ifc` (streamed, not buffered, to handle large files)
- Accept WebSocket connections at `/ws`, track connected clients
- Push JSON commands to all connected WebSocket clients (fire-and-forget async sends)
- Track state: `IsRunning`, `Port`, `Url`

**Public API:**
```csharp
Task StartAsync()           // Start HTTP + WS server
Task StopAsync()            // Stop server, disconnect clients
Task SendHighlightAsync(IReadOnlyList<string> globalIds)
Task SendIsolateAsync(IReadOnlyList<string> globalIds)
Task SendResetAsync()
Task SendCameraFitAsync(IReadOnlyList<string> globalIds)
Task SendReloadAsync()      // Internal: notify browser of model change
bool IsRunning { get; }
string? Url { get; }
```

### Static File Packaging

Viewer files in `src/IfcMcpServer/Viewer/` are included as embedded resources via a wildcard in the `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Viewer\**\*" />
</ItemGroup>
```

ViewerService extracts and serves them using `ManifestEmbeddedFileProvider`.

### ViewerTools (new MCP tool class)

Six tools, all requiring the viewer to be running (except `viewer-open`):

| Tool | Parameters | Description |
|------|-----------|-------------|
| `viewer-open` | none | Start web server, return viewer URL. Requires model loaded. |
| `viewer-close` | none | Stop web server. |
| `viewer-highlight` | `globalIds: string[]` | Highlight elements, dim the rest. |
| `viewer-isolate` | `globalIds: string[]` | Show only specified elements, hide everything else. |
| `viewer-reset` | none | Reset all elements to default visibility and appearance. |
| `viewer-camera` | `globalIds: string[]` | Fly camera to fit specified elements. |

## Browser-Side Viewer

### Tech Stack

- **web-ifc** — WebAssembly IFC parser
- **Three.js** — 3D rendering
- **@thatopen/components** — BIM-specific layer tying web-ifc + Three.js together (element selection, transparency, camera)

### Behavior

- On page load: fetch `/model.ifc`, parse with web-ifc, render full model in Three.js
- Connect WebSocket to `ws://localhost:{port}/ws`
- Listen for commands and apply:
  - **highlight** — semi-transparent colored overlay on specified elements, dim the rest
  - **isolate** — hide all elements except specified ones
  - **reset** — restore all elements to default visibility and appearance
  - **camera-fit** — compute bounding box of specified elements, animate camera to fit
- Standard orbit/pan/zoom camera controls
- Minimal UI: just the 3D viewport, no toolbars or panels

### File Location

Viewer static files live in `src/IfcMcpServer/Viewer/`. JS dependencies loaded via CDN (web-ifc, Three.js, @thatopen/components).

## WebSocket Protocol

### Server → Browser

```json
{
  "action": "highlight",
  "globalIds": ["2O2Fr$t4X7Z...", "3nF..."]
}

{
  "action": "isolate",
  "globalIds": ["2O2Fr$t4X7Z..."]
}

{
  "action": "reset"
}

{
  "action": "camera-fit",
  "globalIds": ["2O2Fr$t4X7Z..."]
}

{
  "action": "reload"
}
```

### Browser → Server

Not used in v1. The WebSocket is bidirectional so pick-to-query can be added later.

### Notes

- The `reload` action is internal-only — triggered by `open-model` when the viewer is running, not exposed as an MCP tool.
- All messages are same-origin (browser loads page and connects WS to the same `localhost:{port}`), so no CORS configuration is needed.

## Lifecycle & Integration

### Dependency Flow

```
open-model  →  viewer-open  →  viewer-highlight / isolate / reset / camera
                              (requires viewer running)
```

### Model Changes

- `open-model` works as today — loads IFC into `ModelSession`
- If the viewer is already running when `open-model` is called with a new file, ViewerService sends a `reload` command over WebSocket so the browser picks up the new model
- `close-model` stops the viewer if it's running

**Integration mechanism:** `ModelTools.OpenModel` and `ModelTools.CloseModel` are modified to accept `ViewerService` via DI. After opening a model, if `ViewerService.IsRunning`, it calls `SendReloadAsync()`. Before closing a model, if `ViewerService.IsRunning`, it calls `StopAsync()`. This keeps the integration in the tools layer (thin wrappers) consistent with the existing pattern.

### Error Handling

- `viewer-open` returns error string if no model is loaded
- `viewer-highlight` / `isolate` / `reset` / `camera` return error string if viewer isn't running
- If the browser disconnects, commands are silently dropped (user refreshes to reconnect)

## Typical Conversation Flow

1. User: "Open /path/to/model.ifc" → `open-model`
2. User: "Show it in the viewer" → `viewer-open` → returns URL
3. User: "Find all external walls" → `list-elements` with property filter → returns table
4. User: "Highlight those in the viewer" → `viewer-highlight` with GlobalIds from step 3
5. User: "Isolate just the ground floor" → `viewer-isolate` with storey elements
6. User: "Reset the view" → `viewer-reset`

## New Dependencies

### .NET (server)
- Switch SDK from `Microsoft.NET.Sdk` to `Microsoft.NET.Sdk.Web` — provides Kestrel, WebSocket middleware, static file serving, and `ManifestEmbeddedFileProvider`. No additional NuGet packages required.

### JavaScript (browser, via CDN)
- `web-ifc` — IFC parser (WASM)
- `three` — 3D rendering
- `@thatopen/components` — BIM components layer
