# Development Guide

## Project Structure

```
IFC/
├── IfcMcpServer.slnx                 # Solution file
├── src/
│   └── IfcMcpServer/
│       ├── IfcMcpServer.csproj
│       ├── Program.cs                 # Host setup, DI, MCP registration
│       ├── Services/
│       │   ├── ModelSession.cs        # Singleton model holder
│       │   ├── ElementQueryService.cs # Filtering and query logic
│       │   ├── QuantityCalculator.cs  # Quantity resolution and aggregation
│       │   └── ExcelExporter.cs       # Excel file generation
│       └── Tools/
│           ├── ModelTools.cs          # open-model, close-model, model-info
│           ├── QueryTools.cs          # list-elements, get-element, list-*
│           ├── QuantityTools.cs       # calculate-quantities
│           └── ExportTools.cs         # export-elements, export-quantities
└── tests/
    └── IfcMcpServer.Tests/
        ├── IfcMcpServer.Tests.csproj
        ├── TestModelBuilder.cs        # Builds test IFC4 model programmatically
        ├── TestData/
        │   └── TestModel.ifc          # Generated fixture
        └── Services/
            ├── ModelSessionTests.cs
            ├── ElementQueryServiceTests.cs
            ├── QuantityCalculatorTests.cs
            └── ExcelExporterTests.cs
```

## Building

```bash
dotnet build
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults/Coverage
```

## Adding a New Tool

1. **Create the tool method** in the appropriate file under `Tools/`, or create a new file:

```csharp
[McpServerTool(Name = "my-tool", ReadOnly = true,
    Description = "Description shown to the AI assistant")]
public static string MyTool(
    ModelSession session,
    ElementQueryService queryService,
    [Description("Parameter description")] string someParam)
{
    if (!session.IsModelLoaded)
        return "Error: No model is currently loaded. Use open-model first.";

    // Delegate to a service method
    var result = queryService.DoSomething(session.CurrentModel!, someParam);

    // Return formatted string
    return FormatResult(result);
}
```

2. **Add business logic** in the appropriate service, or create a new service under `Services/`.

3. **Register new services** in `Program.cs`:

```csharp
builder.Services.AddSingleton<MyNewService>();
```

4. **Add tests** in `tests/IfcMcpServer.Tests/Services/`.

The tool is automatically discovered by `WithToolsFromAssembly()` — no manual registration needed.

## Key Design Rules

- **Tools are thin** — they validate input, delegate to services, and format output. No business logic in tools.
- **Services are testable** — they receive an `IModel` or `IfcStore` and return data. No MCP or formatting concerns.
- **Cross-schema** — always use xBIM interfaces (`IIfcProduct`, `IIfcPropertySet`, etc.) from `Xbim.Ifc4.Interfaces`, never schema-specific classes.
- **Strings for output** — all tools return `string`. Use markdown tables for tabular data.
- **Error strings, not exceptions** — tools catch exceptions and return descriptive error messages.

## Test Model

`TestModelBuilder.cs` programmatically creates a minimal but complete IFC4 model:

| Element | Type | Storey | Properties | Quantities | Classification |
|---------|------|--------|------------|------------|----------------|
| External Wall | IfcWall | Level 1 | IsExternal=True, FireRating=REI60 | Length=5, Height=3, Volume=2.025, Area=15 | Ss_20_05_28 |
| Internal Wall | IfcWall | Level 2 | IsExternal=False, FireRating=REI30 | Length=4, Height=3, Volume=1.62, Area=12 | — |
| Floor Slab | IfcSlab | Level 1 | — | GrossArea=50, GrossVolume=10 | — |
| Main Door | IfcDoor | Level 1 | — | — | — |

This model is saved to `TestData/TestModel.ifc` and shared across all test classes.

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `ModelContextProtocol` | 1.1.0 | MCP server SDK (stdio transport) |
| `Xbim.Essentials` | 6.0.578 | IFC2x3 + IFC4 model parsing |
| `ClosedXML` | 0.105.0 | Excel .xlsx generation |
| `Microsoft.Extensions.Hosting` | 10.0.5 | DI container and app lifetime |
| `xunit` | 2.9.3 | Test framework |
| `coverlet.collector` | 6.0.4 | Code coverage collection |
