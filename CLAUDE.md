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
- **Tests** (`tests/IfcMcpServer.Tests/`) — xUnit tests using a programmatically-built IFC4 model (`TestModelBuilder.cs`).

## Key Conventions

- Target framework: .NET 10.0
- Nullable reference types enabled project-wide
- MCP tools are discovered via `WithToolsFromAssembly()` — add `[McpServerTool]` to any static method
- Services are registered as singletons in `Program.cs`
- All query/export tools check `session.IsModelLoaded` and return an error string if no model is open
- Tools return plain strings (markdown tables for tabular data)
- Cross-schema: use `IIfcProduct`, `IIfcPropertySet`, etc. (xBIM interfaces) — never target IFC2x3 or IFC4 classes directly

## Filter System

Property filters use the format `Pset_Name.PropertyName=Value` with operators: `=`, `!=`, `>`, `<`, `>=`, `<=`.
Classification filters support glob wildcards (`Ss_20*`).

## Test Model

`TestModelBuilder` creates a minimal IFC4 model with 4 elements (2 walls, 1 slab, 1 door), 2 storeys, properties, quantities, and a Uniclass classification. Tests reference this shared fixture.
