# IFC MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io/) server for querying and analyzing IFC (Industry Foundation Classes) building models. Enables AI assistants like Claude to interactively explore BIM data, calculate quantities, and export results to Excel.

## What It Does

Open an IFC model file and use natural language to:

- **Browse** elements by type, classification, storey, or properties
- **Inspect** individual elements with full property and quantity details
- **Calculate** quantity takeoffs grouped by type, classification, storey, or any property
- **Export** element lists and quantity summaries to `.xlsx` spreadsheets

Supports both **IFC2x3** and **IFC4** schemas transparently.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run the Server

```bash
dotnet run --project src/IfcMcpServer
```

### Configure Claude Code

Add to your `.mcp.json`:

```json
{
  "mcpServers": {
    "ifc": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/src/IfcMcpServer"]
    }
  }
}
```

### Configure Claude Desktop

Add to your Claude Desktop config (`~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "ifc": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/src/IfcMcpServer"]
    }
  }
}
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `open-model` | Load an IFC file into memory |
| `close-model` | Unload the current model |
| `model-info` | Show spatial hierarchy (sites, buildings, storeys) |
| `list-elements` | Query elements with filters (type, classification, properties) |
| `get-element` | Get full details for a single element by GlobalId |
| `list-classifications` | List all classification references in the model |
| `list-property-sets` | List property set definitions and their properties |
| `list-storeys` | List building storeys with elevations and element counts |
| `calculate-quantities` | Aggregate quantities grouped by type/classification/storey/property |
| `export-elements` | Export filtered elements to Excel |
| `export-quantities` | Export quantity calculations to Excel |

See the [full tool reference](docs/tool-reference.md) for parameters and examples.

## Example Conversation

> **User:** Open the model at ~/models/office-building.ifc
>
> **Claude:** *Opens the model.* This is an IFC4 file with 2,847 elements across 5 storeys.
>
> **User:** How much wall area is there per storey?
>
> **Claude:** *Calculates quantities grouped by storey for IfcWall.* Here are the results:
>
> | Storey | Elements | NetSideArea |
> |--------|----------|-------------|
> | Ground Floor | 45 | 892.5 m² |
> | First Floor | 38 | 756.2 m² |
> | Second Floor | 38 | 756.2 m² |
>
> **User:** Export that to Excel
>
> **Claude:** *Exports to wall-quantities.xlsx.* Done — 3 groups exported.

## Technology Stack

| Component | Package | Purpose |
|-----------|---------|---------|
| MCP SDK | [ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol) 1.1.0 | .NET MCP server implementation |
| IFC Parsing | [xBIM Essentials](https://docs.xbim.net/) 6.0.578 | IFC2x3 + IFC4 model reading |
| Excel Export | [ClosedXML](https://github.com/ClosedXML/ClosedXML) 0.105.0 | .xlsx file generation |
| Hosting | Microsoft.Extensions.Hosting 10.0.5 | DI and lifetime management |

## Project Structure

```
src/IfcMcpServer/
├── Program.cs                    # Host setup, DI, MCP configuration
├── Services/
│   ├── ModelSession.cs           # Singleton IFC model holder
│   ├── ElementQueryService.cs    # Filter and query elements
│   ├── QuantityCalculator.cs     # Quantity aggregation engine
│   └── ExcelExporter.cs          # Excel file generation
└── Tools/
    ├── ModelTools.cs             # open-model, close-model, model-info
    ├── QueryTools.cs             # list-elements, get-element, list-*
    ├── QuantityTools.cs          # calculate-quantities
    └── ExportTools.cs            # export-elements, export-quantities
```

## Running Tests

```bash
dotnet test
```

## License

Private — all rights reserved.
