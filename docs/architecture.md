# Architecture

## Overview

The IFC MCP Server is a stateful .NET console application that communicates over stdio using the Model Context Protocol. It enables AI assistants to interactively query IFC building models through a conversational workflow: open a model, explore and filter elements, calculate quantities, and export results.

## System Diagram

```
┌─────────────────┐     stdio (JSON-RPC)     ┌──────────────────────┐
│  Claude Code /   │◄──────────────────────►│   IFC MCP Server     │
│  Claude Desktop  │                          │                      │
└─────────────────┘                          │  ┌────────────────┐  │
                                              │  │  MCP Tools     │  │
                                              │  │  (thin layer)  │  │
                                              │  └───────┬────────┘  │
                                              │          │ DI        │
                                              │  ┌───────▼────────┐  │
                                              │  │  Services      │  │
                                              │  │  (logic)       │  │
                                              │  └───────┬────────┘  │
                                              │          │           │
                                              │  ┌───────▼────────┐  │
                                              │  │  ModelSession   │  │
                                              │  │  (IfcStore)     │  │
                                              │  └────────────────┘  │
                                              └──────────────────────┘
                                                         │
                                                         ▼
                                                   ┌──────────┐
                                                   │ .ifc file│
                                                   └──────────┘
```

## Components

### Tools Layer

Static methods decorated with `[McpServerTool]` in the `Tools/` directory. Each tool:

- Receives services via dependency injection (method parameters)
- Validates that a model is loaded
- Delegates to the appropriate service
- Returns a plain string (markdown for tabular data)

Tools are discovered automatically by `WithToolsFromAssembly()` at startup.

| File | Tools | Responsibility |
|------|-------|----------------|
| `ModelTools.cs` | open-model, close-model, model-info | Model lifecycle |
| `QueryTools.cs` | list-elements, get-element, list-classifications, list-property-sets, list-storeys | Element querying |
| `QuantityTools.cs` | calculate-quantities | Quantity aggregation |
| `ExportTools.cs` | export-elements, export-quantities | Excel export |

### Services Layer

Singleton services registered in `Program.cs` that contain all business logic.

| Service | Responsibility |
|---------|----------------|
| `ModelSession` | Holds the single open `IfcStore`. Manages open/close lifecycle and disposal. |
| `ElementQueryService` | Filters elements by type, classification, and properties. Queries classifications, property sets, and storeys. |
| `QuantityCalculator` | Resolves quantities from elements (instance then type level), groups elements, and aggregates numeric values. |
| `ExcelExporter` | Generates `.xlsx` files from element lists or quantity results using ClosedXML. |

### ModelSession (Stateful Core)

The server holds exactly one IFC model in memory at a time. This matches the typical BIM workflow:

1. User points to an IFC file
2. AI explores the model through multiple tool calls
3. User exports what they need
4. Model is closed (or replaced by opening another)

`ModelSession` is a singleton that wraps xBIM's `IfcStore`. Opening a new model disposes the previous one automatically.

## Cross-Schema Support

The server supports both **IFC2x3** and **IFC4** transparently by using xBIM's cross-schema interfaces:

| Interface | Covers |
|-----------|--------|
| `IIfcProduct` | All physical elements |
| `IIfcPropertySet` / `IIfcPropertySingleValue` | Property access |
| `IIfcElementQuantity` / `IIfcPhysicalQuantity` | Quantity access |
| `IIfcClassificationReference` | Classification access |
| `IIfcBuildingStorey` | Spatial structure |

Never reference schema-specific classes (e.g., `Xbim.Ifc4.SharedBldgElements.IfcWall`) directly. Always use the `Xbim.Ifc4.Interfaces` namespace.

## Data Flow

### Query Flow

```
Tool call (list-elements, ifcType="IfcWall")
  → ElementQueryService.QueryElements(model, ifcType="IfcWall")
    → xBIM metadata lookup for type + subtypes
    → Iterate IfcStore instances
    → Apply classification filter (glob → regex)
    → Apply property filters (parse operator, compare values)
    → Return filtered IEnumerable<IIfcProduct>
  → Format as markdown table
  → Return string to MCP client
```

### Quantity Calculation Flow

```
Tool call (calculate-quantities, groupBy="storey", ifcType="IfcWall")
  → QuantityCalculator.Calculate(...)
    → ElementQueryService.QueryElements(...) — get filtered elements
    → GroupElements(...) — bucket by storey name
    → For each group:
      → For each element:
        → ResolveQuantities(element) — instance first, then type
      → Sum quantities across group
    → Return QuantityResult with columns + groups
  → Format as markdown table
  → Return string to MCP client
```

### Export Flow

```
Tool call (export-elements, filePath="output.xlsx")
  → ExcelExporter.ExportElements(model, filePath, queryService, filters)
    → Query elements (same filter logic)
    → Build ElementRow for each (properties + quantities)
    → Discover all unique property/quantity column names
    → Create worksheet with headers + data rows
    → Apply formatting (bold headers, auto-fit)
    → Save .xlsx file
  → Return "Exported N element(s) to output.xlsx"
```

## Error Handling

- **No model loaded:** All tools (except `open-model`) return `"Error: No model is currently loaded. Use open-model first."`
- **File not found:** `open-model` catches `FileNotFoundException` and returns an error string
- **Invalid filter:** Property filter parsing errors are returned as descriptive error strings
- **Element not found:** `get-element` returns `"Error: Element with GlobalId '...' not found."`

All errors are returned as plain strings — no exceptions propagate to the MCP transport layer.

## Dependencies

```
Microsoft.Extensions.Hosting    → DI container, app lifetime
ModelContextProtocol             → MCP server SDK, stdio transport
Xbim.Essentials                  → IFC model parsing (IFC2x3 + IFC4)
ClosedXML                        → Excel .xlsx generation
```
