# Tool Reference

Complete reference for all 17 MCP tools provided by the IFC MCP Server.

## Model Management

### open-model

Load an IFC file into memory. Only one model can be open at a time — opening a new model automatically closes the previous one.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `filePath` | string | Yes | Absolute path to the `.ifc` file |

**Returns:** Summary with schema version, project name, and total element count.

**Example response:**
```
Opened: office-building.ifc
Schema: IFC4
Project: Office Renovation
Elements: 2,847
```

---

### close-model

Unload the current model and free memory. Safe to call when no model is open.

**Parameters:** None

**Returns:** Confirmation message.

---

### model-info

Display the spatial hierarchy of the currently loaded model.

**Parameters:** None

**Returns:** Hierarchical listing of sites, buildings, and storeys with element counts.

**Example response:**
```
# Model: office-building.ifc
Schema: IFC4
Project: Office Renovation

## Spatial Structure
- Site: Default Site
  - Building: Office Building
    - Ground Floor (elevation: 0.0) — 312 elements
    - First Floor (elevation: 3.5) — 287 elements
    - Second Floor (elevation: 7.0) — 245 elements
```

---

## Querying Elements

### list-elements

Query and filter elements in the model. Returns a markdown table.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `ifcType` | string | No | — | IFC entity type (e.g., `IfcWall`, `IfcDoor`). Includes subtypes automatically. |
| `classification` | string | No | — | Classification code or name filter. Supports glob wildcards (`Ss_20*`). Case-insensitive. |
| `propertyFilter` | string[] | No | — | Property filters in `Pset.Property=Value` format. See [Filtering](#filtering). |
| `maxResults` | int | No | 50 | Maximum number of elements to return. |

**Returns:** Markdown table with columns: GlobalId, Name, Type, Classification.

**Example response:**
```markdown
Found 23 element(s) matching filters:

| GlobalId | Name | Type | Classification |
|----------|------|------|----------------|
| 0a1b2c3d | External Wall North | IfcWallStandardCase | Ss_20_05_28 |
| 1b2c3d4e | External Wall East | IfcWallStandardCase | Ss_20_05_28 |
| ... | ... | ... | ... |
```

---

### get-element

Retrieve full details for a single element by its GlobalId.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `globalId` | string | Yes | The IFC GlobalId of the element |

**Returns:** Formatted text with:
- Basic info (name, type, storey, description)
- All property sets and their properties
- All quantity sets and their values
- Classification references
- Type object information

---

### list-classifications

List all classification references found in the model.

**Parameters:** None

**Returns:** Markdown table with columns: System, Code, Name.

**Example response:**
```markdown
| System | Code | Name |
|--------|------|------|
| Uniclass 2015 | Ss_20_05_28 | Concrete walls |
| Uniclass 2015 | Ss_25_10_30 | Floor finishes |
```

---

### list-property-sets

List property set definitions and the property names they contain.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `ifcType` | string | No | Filter to property sets used by this element type |

**Returns:** List of property set names with their property names. Useful for discovering available properties before filtering.

---

### list-storeys

List building storeys with elevations and element counts.

**Parameters:** None

**Returns:** Markdown table with columns: Storey, Elevation, ElementCount.

---

## Quantity Calculation

### calculate-quantities

Aggregate quantities from matched elements, grouped by a chosen dimension.

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `groupBy` | string | Yes | — | Grouping dimension. See [Grouping](#grouping). |
| `ifcType` | string | No | — | Filter by IFC type |
| `classification` | string | No | — | Filter by classification |
| `propertyFilter` | string[] | No | — | Property filters |
| `quantityNames` | string[] | No | all | Specific quantity names to include (e.g., `["NetSideArea", "GrossVolume"]`) |

**Returns:** Markdown table with columns: Group, ElementCount, and one column per quantity.

**Example response:**
```markdown
| Group | ElementCount | NetSideArea | GrossVolume |
|-------|-------------|-------------|-------------|
| Ground Floor | 45 | 892.50 | 312.38 |
| First Floor | 38 | 756.20 | 264.67 |
```

---

## Export

### export-elements

Export filtered elements to an Excel (`.xlsx`) file. Uses the same filter parameters as `list-elements`.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `filePath` | string | Yes | Output path for the `.xlsx` file |
| `ifcType` | string | No | Filter by IFC type |
| `classification` | string | No | Filter by classification |
| `propertyFilter` | string[] | No | Property filters |

**Returns:** Confirmation with the number of elements exported.

**Excel columns:** GlobalId, Name, IfcType, Storey, Classification, ClassificationName, plus dynamic columns for all properties and quantities found on the matched elements.

---

### export-quantities

Export quantity calculation results to an Excel (`.xlsx`) file. Uses the same parameters as `calculate-quantities`.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `filePath` | string | Yes | Output path for the `.xlsx` file |
| `groupBy` | string | Yes | Grouping dimension |
| `ifcType` | string | No | Filter by IFC type |
| `classification` | string | No | Filter by classification |
| `propertyFilter` | string[] | No | Property filters |
| `quantityNames` | string[] | No | Specific quantities to include |

**Returns:** Confirmation with the number of groups exported.

**Excel columns:** Group, ElementCount, plus one column per quantity.

---

## Filtering

### IFC Type Filter

The `ifcType` parameter matches the IFC entity type name. Subtypes are included automatically — filtering by `IfcWall` also returns `IfcWallStandardCase` elements.

Common types: `IfcWall`, `IfcSlab`, `IfcDoor`, `IfcWindow`, `IfcColumn`, `IfcBeam`, `IfcRoof`, `IfcStair`, `IfcSpace`, `IfcFurnishingElement`.

### Classification Filter

The `classification` parameter matches against both the classification **Code** and **Name** fields. It is case-insensitive and supports glob wildcards:

| Pattern | Matches |
|---------|---------|
| `Ss_20_05_28` | Exact code match |
| `Ss_20*` | All codes starting with `Ss_20` |
| `*concrete*` | Codes or names containing "concrete" |

### Property Filter

Each entry in the `propertyFilter` array uses the format:

```
PropertySetName.PropertyName<operator>Value
```

**Supported operators:**

| Operator | Description | Example |
|----------|-------------|---------|
| `=` | Equals | `Pset_WallCommon.IsExternal=True` |
| `!=` | Not equals | `Pset_WallCommon.FireRating!=REI60` |
| `>` | Greater than (numeric) | `Qto_WallBaseQuantities.Length>5.0` |
| `<` | Less than (numeric) | `Qto_WallBaseQuantities.Height<3.0` |
| `>=` | Greater or equal (numeric) | `Qto_SlabBaseQuantities.GrossArea>=50` |
| `<=` | Less or equal (numeric) | `Qto_WallBaseQuantities.GrossVolume<=10` |

- String comparison is case-insensitive for `=` and `!=`
- Boolean values match case-insensitively (`True`, `true`, `TRUE`)
- Numeric operators require a valid number as the value
- Multiple filters are combined with AND logic

---

## Grouping

The `groupBy` parameter for `calculate-quantities` and `export-quantities` supports:

| Value | Groups by | Example keys |
|-------|-----------|--------------|
| `type` | IFC entity type name | `IfcWall`, `IfcSlab` |
| `classification` | Classification code | `Ss_20_05_28`, `(unclassified)` |
| `storey` | Building storey name | `Ground Floor`, `First Floor` |
| `property:Pset.Prop` | A specific property value | `property:Pset_WallCommon.FireRating` → `REI60`, `REI30` |

---

## Quantity Resolution

Quantities are resolved with the following priority:

1. **Instance-level** — quantities defined directly on the element (in `IfcElementQuantity`)
2. **Type-level** — quantities inherited from the element's `IfcTypeObject`
3. **Not found** — blank cell / zero in aggregation

When quantities are aggregated across a group, values are summed.

---

## 3D Viewer

The viewer tools control a browser-based 3D visualization of the loaded IFC model. The viewer runs on an embedded web server (separate from the MCP transport) and communicates via WebSocket.

### viewer-open

Start the 3D web viewer and return the URL. Requires a model to be loaded first.

**Parameters:** None

**Returns:** The viewer URL (e.g., `http://localhost:52341`).

**Notes:**
- The viewer starts on a random available port
- Open the returned URL in a browser to see the 3D model
- The browser parses the IFC file client-side using web-ifc (WebAssembly)
- Idempotent — calling again when already running returns the same URL

---

### viewer-close

Stop the 3D web viewer.

**Parameters:** None

**Returns:** Confirmation message.

---

### viewer-highlight

Highlight specific elements in the viewer by their GlobalId. Non-highlighted elements are dimmed to gray.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `globalIds` | string[] | Yes | Array of IFC GlobalIds to highlight |

**Returns:** Confirmation with element count.

**Behavior:** All elements are dimmed to gray, then the specified elements are restored to their original color. Use `viewer-reset` to restore the view.

---

### viewer-isolate

Show only the specified elements — everything else is hidden.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `globalIds` | string[] | Yes | Array of IFC GlobalIds to isolate |

**Returns:** Confirmation with element count.

**Behavior:** All elements are hidden, then only the specified elements are made visible. Use `viewer-reset` to restore the view.

---

### viewer-reset

Reset the viewer to show all elements with default visibility and colors.

**Parameters:** None

**Returns:** Confirmation message.

**Behavior:** Restores all element visibility (undoes isolate) and resets all colors (undoes highlight).

---

### viewer-camera

Fly the camera to fit specific elements in the view.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `globalIds` | string[] | Yes | Array of IFC GlobalIds to fit in the camera view |

**Returns:** Confirmation with element count.

**Behavior:** Computes the bounding box of the specified elements and animates the camera to fit them in view. Can be combined with highlight or isolate.

---

### Typical Viewer Workflow

```
1. open-model    → Load an IFC file
2. viewer-open   → Start the viewer, open URL in browser
3. list-elements → Query elements, get GlobalIds
4. viewer-highlight globalIds → Highlight found elements (rest dimmed)
5. viewer-camera globalIds   → Zoom to highlighted elements
6. viewer-reset              → Restore full view
7. viewer-isolate globalIds  → Show only specific elements
8. viewer-reset              → Restore again
```
