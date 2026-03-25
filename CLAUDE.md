# IFC MCP Server — Development Guide

## Build & Test

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults/Coverage

# Run the MCP server (stdio transport)
dotnet run --project src/IfcMcpServer
```

## Architecture

**Stateful session server** — one IFC model loaded at a time via `ModelSession` singleton.

- **Tools** (`src/IfcMcpServer/Tools/`) — thin MCP method wrappers decorated with `[McpServerTool]`. No business logic here.
- **Services** (`src/IfcMcpServer/Services/`) — all business logic. Injected into tools via DI.
- **Viewer** (`src/IfcMcpServer/Viewer/`) — browser-based 3D viewer (HTML/JS/CSS) served as embedded resources. Uses That Open Engine (web-ifc + Three.js) via CDN.
- **Tests** (`tests/IfcMcpServer.Tests/`) — xUnit tests using a programmatically-built IFC4 model (`TestModelBuilder.cs`).

## Key Conventions

- Target framework: .NET 10.0, SDK: `Microsoft.NET.Sdk.Web`
- Nullable reference types enabled project-wide
- MCP tools are discovered via `WithToolsFromAssembly()` — add `[McpServerTool]` to any static method
- Services are registered as singletons in `Program.cs`
- All query/export tools check `session.IsModelLoaded` and return an error string if no model is open
- Viewer tools check `viewer.IsRunning` and return an error string if the viewer isn't started
- Tools return plain strings (markdown tables for tabular data)
- Cross-schema: use `IIfcProduct`, `IIfcPropertySet`, etc. (xBIM interfaces) — never target IFC2x3 or IFC4 classes directly

## Viewer

`ViewerService` manages an embedded Kestrel web server (separate from the MCP stdio transport) that serves the browser-based 3D viewer. It starts on a random port when `viewer-open` is called.

- Kestrel logging is suppressed to avoid corrupting the MCP stdio channel
- Static viewer files are embedded resources served via `ManifestEmbeddedFileProvider`
- The raw `.ifc` file is served at `/model.ifc`
- WebSocket at `/ws` pushes JSON commands (highlight, isolate, reset, camera-fit, reload) to the browser
- Browser-side uses That Open Engine (@thatopen/components + @thatopen/fragments) loaded via CDN import maps
- `open-model` sends a reload command if the viewer is running; `close-model` stops the viewer

## Filter System

Property filters use the format `Pset_Name.PropertyName=Value` with operators: `=`, `!=`, `>`, `<`, `>=`, `<=`.
Classification filters support glob wildcards (`Ss_20*`).

## Test Model

`TestModelBuilder` creates a minimal IFC4 model with 4 elements (2 walls, 1 slab, 1 door), 2 storeys, properties, quantities, and a Uniclass classification. Tests reference this shared fixture.
