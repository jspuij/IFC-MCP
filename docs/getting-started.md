# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later

## Installation

Clone the repository and build:

```bash
git clone <repository-url>
cd IFC
dotnet build
```

## Running the Server

The server communicates over stdio and is designed to be launched by an MCP client (Claude Code, Claude Desktop, or any MCP-compatible application).

```bash
dotnet run --project src/IfcMcpServer
```

## Client Configuration

### Claude Code

Create or edit `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "ifc": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/src/IfcMcpServer"]
    }
  }
}
```

### Claude Desktop

Edit your Claude Desktop configuration file:

- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "ifc": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/src/IfcMcpServer"]
    }
  }
}
```

### Other MCP Clients

Any MCP client that supports stdio transport can launch the server. The server expects no command-line arguments beyond the standard `dotnet run` invocation.

## Typical Workflow

### 1. Open a Model

Ask Claude to open an IFC file:

> "Open the model at /path/to/building.ifc"

Claude will call `open-model` and show you a summary of the file (schema version, project name, element count).

### 2. Explore the Model

Discover what's in the model:

> "What storeys does this building have?"
> "List all the classification systems used"
> "What property sets are available on walls?"

### 3. Query Elements

Filter and browse elements:

> "Show me all external walls"
> "List doors on the ground floor"
> "Find elements classified as Ss_20*"

### 4. Calculate Quantities

Aggregate quantities across elements:

> "Calculate wall areas per storey"
> "What's the total volume of concrete walls?"
> "Group slab quantities by classification"

### 5. Export Results

Save results to Excel for further analysis:

> "Export all wall elements to walls.xlsx"
> "Export the quantity breakdown to quantities.xlsx"

### 6. Close the Model

> "Close the model"

Or simply open another model — the previous one is closed automatically.

## Tips

- **Use `list-property-sets`** to discover available properties before filtering. Different IFC files use different property set names.
- **Use `list-classifications`** to see what classification systems are present and what codes to filter by.
- **Glob wildcards** work in classification filters: `Ss_20*` matches all codes starting with `Ss_20`.
- **Property filters** support comparison operators: `Qto_WallBaseQuantities.Length>5.0` finds walls longer than 5 meters.
- **Group by property** for custom breakdowns: grouping by `property:Pset_WallCommon.FireRating` shows quantities per fire rating.
