# IFC MCP Server — Design Spec

## Overview

A .NET 8 console application that serves as an MCP (Model Context Protocol) server over stdio. It uses the XBIM toolkit to read IFC2x3 and IFC4 files and exposes tools for querying elements, calculating quantities, and exporting data to Excel.

**Primary use case:** Interactive quantity takeoff — open an IFC model, filter elements by type/classification/properties, aggregate quantities, and export results to `.xlsx` flat tables.

**User:** Single user via Claude Code / Claude Desktop.

## Technology Stack

| Component | Package | Purpose |
|-----------|---------|---------|
| MCP SDK | `ModelContextProtocol` | Official .NET MCP SDK, stdio transport |
| IFC Reading | `Xbim.Essentials` | IFC2x3 + IFC4 parsing, properties, quantities, classifications |
| Excel Export | `ClosedXML` | Write `.xlsx` files, MIT licensed |
| Hosting | `Microsoft.Extensions.Hosting` | DI, lifetime management |

Geometry-derived calculations (`Xbim.Geometry`) are out of scope for the initial version.

## Architecture

### Approach: Stateful Session Server

One IFC model is loaded in memory at a time. The user opens a model, explores it with query tools, calculates quantities, and exports results. This matches the conversational workflow: open → query → refine → export.

### Components

```
Program.cs (Host + DI + MCP setup)
    │
    ├── Services/
    │   ├── ModelSession          ← singleton, holds current IfcStore
    │   ├── ElementQueryService   ← filtering, element retrieval
    │   ├── QuantityCalculator    ← aggregation, grouping
    │   └── ExcelExporter         ← ClosedXML flat table writer
    │
    └── Tools/
        ├── ModelTools            ← open-model, close-model, model-info
        ├── QueryTools            ← list-elements, get-element, list-classifications, etc.
        ├── QuantityTools         ← calculate-quantities
        └── ExportTools           ← export-elements, export-quantities
```

Tools are thin wrappers — they parse MCP parameters, call services, and format return values. Business logic lives in the services. `ModelSession` is a DI singleton shared across all tools.

## MCP Tools

### Model Management

| Tool | Parameters | Returns |
|------|-----------|---------|
| `open-model` | `filePath` | Model summary: schema version, element counts by type, project name |
| `close-model` | — | Confirmation |
| `model-info` | — | Current model metadata (schema, file path, site, building, storeys) |

### Query Tools

| Tool | Parameters | Returns |
|------|-----------|---------|
| `list-elements` | `ifcType?`, `classification?`, `propertyFilter?`, `maxResults?` (default 50) | Table: GlobalId, Name, Type, Classification |
| `get-element` | `globalId` | Full detail: all property sets, quantity sets, classification refs, type info |
| `list-classifications` | — | All classification systems and references in the model |
| `list-property-sets` | `ifcType?` | Property set names and their properties for elements of that type |
| `list-storeys` | — | Building storeys with contained element counts |

### Quantity Calculation

| Tool | Parameters | Returns |
|------|-----------|---------|
| `calculate-quantities` | `ifcType?`, `classification?`, `propertyFilter?`, `groupBy`, `quantityNames?` | Grouped table with summed quantities and element count |

### Export

| Tool | Parameters | Returns |
|------|-----------|---------|
| `export-elements` | `filePath`, + same filters as `list-elements` | Exports elements with all properties/quantities to .xlsx |
| `export-quantities` | `filePath`, + same params as `calculate-quantities` | Exports quantity results to .xlsx |

## Filter System

All filters are optional and AND-ed together when combined:

- **`ifcType`** — IFC entity name (e.g. `"IfcWall"`, `"IfcSlab"`). Matches subtypes (e.g. `"IfcWall"` includes `IfcWallStandardCase`).
- **`classification`** — matches classification reference codes or names. Supports wildcard: `"Ss_20*"`.
- **`propertyFilter`** — format: `"PsetName.PropertyName=Value"`. Operators: `=`, `!=`, `>`, `<`, `>=`, `<=`. Multiple filters comma-separated: `"Pset_WallCommon.IsExternal=True,Pset_WallCommon.FireRating=REI60"`.

## Quantity Resolution

Quantities are resolved in priority order:

1. **Instance-level** — from `IfcElementQuantity` sets on the element
2. **Type-level** — inherited from the element's `IfcTypeObject` if not present at instance level
3. **Missing** — if a requested quantity name isn't found, the cell is left blank

## Grouping

The `groupBy` parameter in `calculate-quantities`:

- `"type"` — group by IFC entity type name
- `"classification"` — group by classification reference code
- `"storey"` — group by containing `IfcBuildingStorey`
- `"property:PsetName.PropertyName"` — group by a property value

Each group row includes summed numeric quantities and element count.

## Excel Export Format

### export-elements

Flat table with columns:

| GlobalId | Name | IfcType | Storey | Classification | ClassificationName | *PsetName.PropertyName...* | *QtoName.QuantityName...* |

Property and quantity columns are dynamically generated based on what exists on matched elements.

### export-quantities

| Group | ElementCount | *QuantityName1* | *QuantityName2* | ... |

### Formatting

- Bold header row, light grey background
- Auto-fitted column widths
- Numeric values stored as numbers (not strings)
- File saved to the user-specified `filePath`

## Project Structure

```
IFC/
├── src/
│   └── IfcMcpServer/
│       ├── IfcMcpServer.csproj
│       ├── Program.cs
│       ├── Services/
│       │   ├── ModelSession.cs
│       │   ├── ElementQueryService.cs
│       │   ├── QuantityCalculator.cs
│       │   └── ExcelExporter.cs
│       └── Tools/
│           ├── ModelTools.cs
│           ├── QueryTools.cs
│           ├── QuantityTools.cs
│           └── ExportTools.cs
└── tests/
    └── IfcMcpServer.Tests/
        ├── IfcMcpServer.Tests.csproj
        ├── Services/
        │   ├── ElementQueryServiceTests.cs
        │   ├── QuantityCalculatorTests.cs
        │   └── ExcelExporterTests.cs
        └── TestData/
            └── (small test IFC files)
```

## Out of Scope (Initial Version)

- Geometry-derived quantity calculations (requires `Xbim.Geometry`)
- Multi-model sessions
- HTTP/SSE transport
- Write-back / model modification
