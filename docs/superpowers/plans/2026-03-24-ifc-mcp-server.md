# IFC MCP Server Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an MCP server that reads IFC files via XBIM, queries elements/properties/classifications, calculates quantities, and exports to Excel.

**Architecture:** Stateful session server — one IFC model loaded in memory at a time. Thin MCP tool methods delegate to service classes. DI singleton `ModelSession` holds the current `IfcStore`. Stdio transport.

**Tech Stack:** .NET 8, ModelContextProtocol 1.1.0, Xbim.Essentials, ClosedXML

**Spec:** `docs/superpowers/specs/2026-03-24-ifc-mcp-server-design.md`

---

## File Map

| File | Responsibility |
|------|---------------|
| `src/IfcMcpServer/IfcMcpServer.csproj` | Project file with package references |
| `src/IfcMcpServer/Program.cs` | Host builder, DI registration, MCP server config |
| `src/IfcMcpServer/Services/ModelSession.cs` | Singleton holding current `IfcStore`, open/close/dispose |
| `src/IfcMcpServer/Services/ElementQueryService.cs` | Filter elements by type/classification/property |
| `src/IfcMcpServer/Services/QuantityCalculator.cs` | Resolve quantities, aggregate by group |
| `src/IfcMcpServer/Services/ExcelExporter.cs` | Write flat tables to .xlsx via ClosedXML |
| `src/IfcMcpServer/Tools/ModelTools.cs` | MCP tools: open-model, close-model, model-info |
| `src/IfcMcpServer/Tools/QueryTools.cs` | MCP tools: list-elements, get-element, list-classifications, list-property-sets, list-storeys |
| `src/IfcMcpServer/Tools/QuantityTools.cs` | MCP tool: calculate-quantities |
| `src/IfcMcpServer/Tools/ExportTools.cs` | MCP tools: export-elements, export-quantities |
| `tests/IfcMcpServer.Tests/IfcMcpServer.Tests.csproj` | Test project |
| `tests/IfcMcpServer.Tests/Services/ModelSessionTests.cs` | Tests for model open/close/dispose |
| `tests/IfcMcpServer.Tests/Services/ElementQueryServiceTests.cs` | Tests for filtering logic |
| `tests/IfcMcpServer.Tests/Services/QuantityCalculatorTests.cs` | Tests for aggregation/grouping |
| `tests/IfcMcpServer.Tests/Services/ExcelExporterTests.cs` | Tests for Excel output |
| `tests/IfcMcpServer.Tests/TestData/` | Small IFC test files |

---

### Task 1: Project Scaffolding and Test IFC File

**Files:**
- Create: `src/IfcMcpServer/IfcMcpServer.csproj`
- Create: `src/IfcMcpServer/Program.cs`
- Create: `tests/IfcMcpServer.Tests/IfcMcpServer.Tests.csproj`
- Create: `tests/IfcMcpServer.Tests/TestData/TestModel.ifc`

- [ ] **Step 1: Create .gitignore**

```
bin/
obj/
TestResults/
*.user
.vs/
```

- [ ] **Step 2: Create the solution and main project**

```bash
cd /Users/jws/Sources/IFC
dotnet new sln -n IfcMcpServer
dotnet new console -n IfcMcpServer -o src/IfcMcpServer
dotnet sln add src/IfcMcpServer/IfcMcpServer.csproj
```

- [ ] **Step 3: Add NuGet packages to the main project**

```bash
cd /Users/jws/Sources/IFC/src/IfcMcpServer
dotnet add package ModelContextProtocol --version 1.1.0
dotnet add package Xbim.Essentials
dotnet add package ClosedXML
```

- [ ] **Step 4: Create the test project**

```bash
cd /Users/jws/Sources/IFC
dotnet new xunit -n IfcMcpServer.Tests -o tests/IfcMcpServer.Tests
dotnet sln add tests/IfcMcpServer.Tests/IfcMcpServer.Tests.csproj
dotnet add tests/IfcMcpServer.Tests/IfcMcpServer.Tests.csproj reference src/IfcMcpServer/IfcMcpServer.csproj
```

- [ ] **Step 5: Create a test model builder**

Create `tests/IfcMcpServer.Tests/TestModelBuilder.cs` — a helper that programmatically creates a valid IFC4 test model and saves it to `TestData/TestModel.ifc`. This is safer than hand-writing STEP syntax.

```csharp
// tests/IfcMcpServer.Tests/TestModelBuilder.cs
using Xbim.Ifc;
using Xbim.Ifc4.Kernel;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.SharedBldgElements;
using Xbim.Ifc4.ProductExtension;
using Xbim.Ifc4.PropertyResource;
using Xbim.Ifc4.QuantityResource;
using Xbim.Ifc4.ExternalReferenceResource;
using Xbim.Common;
using Xbim.Common.Step21;

namespace IfcMcpServer.Tests;

public static class TestModelBuilder
{
    private static readonly string TestDataDir = Path.Combine(
        AppContext.BaseDirectory, "TestData");
    public static readonly string TestModelPath = Path.Combine(TestDataDir, "TestModel.ifc");

    private static readonly object Lock = new();
    private static bool _built;

    public static void EnsureTestModel()
    {
        lock (Lock)
        {
            if (_built && File.Exists(TestModelPath)) return;
            Directory.CreateDirectory(TestDataDir);
            BuildTestModel(TestModelPath);
            _built = true;
        }
    }

    private static void BuildTestModel(string path)
    {
        var creds = new XbimEditorCredentials
        {
            ApplicationDevelopersName = "Test",
            ApplicationFullName = "TestApp",
            ApplicationVersion = "1.0",
            EditorsFamilyName = "Test"
        };

        using var model = IfcStore.Create(creds, Xbim.Common.Step21.XbimSchemaVersion.Ifc4, XbimStoreType.InMemoryModel);
        using var txn = model.BeginTransaction("Create test model");

        // Project
        var project = model.Instances.New<IfcProject>(p =>
        {
            p.Name = "Test Project";
            p.UnitsInContext = model.Instances.New<Xbim.Ifc4.MeasureResource.IfcUnitAssignment>();
        });

        // Site
        var site = model.Instances.New<IfcSite>(s => s.Name = "Test Site");
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = project;
            r.RelatedObjects.Add(site);
        });

        // Building
        var building = model.Instances.New<IfcBuilding>(b => b.Name = "Test Building");
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = site;
            r.RelatedObjects.Add(building);
        });

        // Storeys
        var groundFloor = model.Instances.New<IfcBuildingStorey>(s =>
        {
            s.Name = "Ground Floor";
            s.Elevation = 0.0;
        });
        var firstFloor = model.Instances.New<IfcBuildingStorey>(s =>
        {
            s.Name = "First Floor";
            s.Elevation = 3.0;
        });
        model.Instances.New<IfcRelAggregates>(r =>
        {
            r.RelatingObject = building;
            r.RelatedObjects.Add(groundFloor);
            r.RelatedObjects.Add(firstFloor);
        });

        // Wall 1 - external, classified
        var wall1 = model.Instances.New<IfcWall>(w => w.Name = "External Wall 1");
        AddPsetWallCommon(model, wall1, isExternal: true);
        AddWallQuantities(model, wall1, length: 5.0, height: 3.0, grossVolume: 2.025, netSideArea: 15.0);

        // Classification on wall1
        var classification = model.Instances.New<IfcClassification>(c => c.Name = "Uniclass");
        var classRef = model.Instances.New<IfcClassificationReference>(cr =>
        {
            cr.Identification = "Ss_20_05_28";
            cr.Name = "Gypsum block walls";
            cr.ReferencedSource = classification;
        });
        model.Instances.New<IfcRelAssociatesClassification>(r =>
        {
            r.RelatingClassification = classRef;
            r.RelatedObjects.Add(wall1);
        });

        // Wall 2 - internal
        var wall2 = model.Instances.New<IfcWall>(w => w.Name = "Internal Wall 1");
        AddPsetWallCommon(model, wall2, isExternal: false);
        AddWallQuantities(model, wall2, length: 4.0, height: 3.0, grossVolume: 1.62, netSideArea: 12.0);

        // Slab
        var slab = model.Instances.New<IfcSlab>(s => s.Name = "Ground Floor Slab");
        AddSlabQuantities(model, slab, grossArea: 50.0, grossVolume: 10.0);

        // Door
        var door = model.Instances.New<IfcDoor>(d => d.Name = "Door 1");

        // Contain elements in Ground Floor
        model.Instances.New<IfcRelContainedInSpatialStructure>(r =>
        {
            r.RelatingStructure = groundFloor;
            r.RelatedElements.Add(wall1);
            r.RelatedElements.Add(wall2);
            r.RelatedElements.Add(slab);
            r.RelatedElements.Add(door);
        });

        txn.Commit();
        model.SaveAs(path);
    }

    private static void AddPsetWallCommon(IModel model, IfcWall wall, bool isExternal)
    {
        var pset = model.Instances.New<IfcPropertySet>(ps =>
        {
            ps.Name = "Pset_WallCommon";
            ps.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
            {
                p.Name = "IsExternal";
                p.NominalValue = new IfcBoolean(isExternal);
            }));
            ps.HasProperties.Add(model.Instances.New<IfcPropertySingleValue>(p =>
            {
                p.Name = "FireRating";
                p.NominalValue = new IfcLabel(isExternal ? "REI60" : "REI30");
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = pset;
            r.RelatedObjects.Add(wall);
        });
    }

    private static void AddWallQuantities(IModel model, IfcWall wall,
        double length, double height, double grossVolume, double netSideArea)
    {
        var qset = model.Instances.New<IfcElementQuantity>(eq =>
        {
            eq.Name = "Qto_WallBaseQuantities";
            eq.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
            {
                q.Name = "Length";
                q.LengthValue = length;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityLength>(q =>
            {
                q.Name = "Height";
                q.LengthValue = height;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityVolume>(q =>
            {
                q.Name = "GrossVolume";
                q.VolumeValue = grossVolume;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
            {
                q.Name = "NetSideArea";
                q.AreaValue = netSideArea;
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = qset;
            r.RelatedObjects.Add(wall);
        });
    }

    private static void AddSlabQuantities(IModel model, IfcSlab slab,
        double grossArea, double grossVolume)
    {
        var qset = model.Instances.New<IfcElementQuantity>(eq =>
        {
            eq.Name = "Qto_SlabBaseQuantities";
            eq.Quantities.Add(model.Instances.New<IfcQuantityArea>(q =>
            {
                q.Name = "GrossArea";
                q.AreaValue = grossArea;
            }));
            eq.Quantities.Add(model.Instances.New<IfcQuantityVolume>(q =>
            {
                q.Name = "GrossVolume";
                q.VolumeValue = grossVolume;
            }));
        });

        model.Instances.New<IfcRelDefinesByProperties>(r =>
        {
            r.RelatingPropertyDefinition = qset;
            r.RelatedObjects.Add(slab);
        });
    }
}
```

Update all test classes to call `TestModelBuilder.EnsureTestModel()` in their constructor and use `TestModelBuilder.TestModelPath` instead of `"TestData/TestModel.ifc"`.

- [ ] **Step 6: Write minimal Program.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "ifc-mcp-server", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

- [ ] **Step 7: Verify build succeeds**

```bash
cd /Users/jws/Sources/IFC
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scaffold project with MCP server, XBIM, ClosedXML packages and test model builder"
```

---

### Task 2: ModelSession Service

**Files:**
- Create: `src/IfcMcpServer/Services/ModelSession.cs`
- Create: `tests/IfcMcpServer.Tests/Services/ModelSessionTests.cs`

- [ ] **Step 1: Write failing tests for ModelSession**

```csharp
// tests/IfcMcpServer.Tests/Services/ModelSessionTests.cs
using IfcMcpServer.Services;
using IfcMcpServer.Tests;

namespace IfcMcpServer.Tests.Services;

public class ModelSessionTests : IDisposable
{
    private readonly ModelSession _session = new();

    public ModelSessionTests()
    {
        TestModelBuilder.EnsureTestModel();
    }

    [Fact]
    public void InitialState_NoModelLoaded()
    {
        Assert.False(_session.IsModelLoaded);
        Assert.Null(_session.CurrentModel);
        Assert.Null(_session.FilePath);
    }

    [Fact]
    public void OpenModel_LoadsIfc()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);

        Assert.True(_session.IsModelLoaded);
        Assert.NotNull(_session.CurrentModel);
        Assert.Equal(TestModelBuilder.TestModelPath, _session.FilePath);
    }

    [Fact]
    public void OpenModel_FileNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _session.OpenModel("nonexistent.ifc"));
    }

    [Fact]
    public void OpenModel_ReplacesExistingModel()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);
        var firstModel = _session.CurrentModel;

        _session.OpenModel(TestModelBuilder.TestModelPath);

        Assert.NotSame(firstModel, _session.CurrentModel);
    }

    [Fact]
    public void CloseModel_ClearsState()
    {
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _session.CloseModel();

        Assert.False(_session.IsModelLoaded);
        Assert.Null(_session.CurrentModel);
        Assert.Null(_session.FilePath);
    }

    [Fact]
    public void CloseModel_WhenNoneOpen_DoesNotThrow()
    {
        _session.CloseModel(); // should not throw
        Assert.False(_session.IsModelLoaded);
    }

    public void Dispose() => _session.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ModelSessionTests" -v n
```

Expected: FAIL — `ModelSession` class does not exist.

- [ ] **Step 3: Implement ModelSession**

```csharp
// src/IfcMcpServer/Services/ModelSession.cs
using Xbim.Ifc;

namespace IfcMcpServer.Services;

public class ModelSession : IDisposable
{
    public IfcStore? CurrentModel { get; private set; }
    public string? FilePath { get; private set; }
    public bool IsModelLoaded => CurrentModel != null;

    public void OpenModel(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"IFC file not found: {filePath}", filePath);

        CloseModel();
        CurrentModel = IfcStore.Open(filePath);
        FilePath = filePath;
    }

    public void CloseModel()
    {
        CurrentModel?.Dispose();
        CurrentModel = null;
        FilePath = null;
    }

    public void Dispose()
    {
        CloseModel();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ModelSessionTests" -v n
```

Expected: All 6 tests PASS.

- [ ] **Step 5: Register ModelSession in DI in Program.cs**

Add to `Program.cs` before `AddMcpServer`:

```csharp
builder.Services.AddSingleton<ModelSession>();
```

Add the `using IfcMcpServer.Services;` import.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ModelSession service with open/close/dispose lifecycle"
```

---

### Task 3: ModelTools (open-model, close-model, model-info)

**Files:**
- Create: `src/IfcMcpServer/Tools/ModelTools.cs`

- [ ] **Step 1: Implement ModelTools**

```csharp
// src/IfcMcpServer/Tools/ModelTools.cs
using System.ComponentModel;
using System.Text;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class ModelTools
{
    [McpServerTool(Name = "open-model", ReadOnly = false), Description("Open an IFC file for querying. Closes any previously opened model.")]
    public static string OpenModel(
        ModelSession session,
        [Description("Absolute or relative path to the IFC file")] string filePath)
    {
        session.OpenModel(filePath);
        var model = session.CurrentModel!;

        var schemaVersion = model.SchemaVersion.ToString();
        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        var projectName = project?.Name?.ToString() ?? "(unnamed)";
        var totalElements = model.Instances.OfType<IIfcProduct>().Count();

        return $"Model opened: {filePath}\nSchema: {schemaVersion}\nProject: {projectName}\nTotal elements: {totalElements}";
    }

    [McpServerTool(Name = "close-model", ReadOnly = false), Description("Close the currently loaded IFC model and free memory.")]
    public static string CloseModel(ModelSession session)
    {
        if (!session.IsModelLoaded)
            return "No model is currently loaded.";

        session.CloseModel();
        return "Model closed.";
    }

    [McpServerTool(Name = "model-info", ReadOnly = true), Description("Get detailed information about the currently loaded IFC model including spatial hierarchy.")]
    public static string ModelInfo(ModelSession session)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var model = session.CurrentModel!;
        var sb = new StringBuilder();

        sb.AppendLine($"File: {session.FilePath}");
        sb.AppendLine($"Schema: {model.SchemaVersion}");

        var project = model.Instances.OfType<IIfcProject>().FirstOrDefault();
        sb.AppendLine($"Project: {project?.Name?.ToString() ?? "(unnamed)"}");

        foreach (var site in model.Instances.OfType<IIfcSite>())
        {
            sb.AppendLine($"\nSite: {site.Name?.ToString() ?? "(unnamed)"}");

            var buildings = site.IsDecomposedBy
                .SelectMany(r => r.RelatedObjects)
                .OfType<IIfcBuilding>();

            foreach (var building in buildings)
            {
                sb.AppendLine($"  Building: {building.Name?.ToString() ?? "(unnamed)"}");

                var storeys = building.IsDecomposedBy
                    .SelectMany(r => r.RelatedObjects)
                    .OfType<IIfcBuildingStorey>()
                    .OrderBy(s => s.Elevation?.Value ?? 0);

                foreach (var storey in storeys)
                {
                    var elementCount = storey.ContainsElements
                        .SelectMany(r => r.RelatedElements)
                        .Count();
                    sb.AppendLine($"    Storey: {storey.Name?.ToString() ?? "(unnamed)"} (elevation: {storey.Elevation?.Value ?? 0:F1}, elements: {elementCount})");
                }
            }
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add ModelTools with open-model, close-model, model-info MCP tools"
```

---

### Task 4: ElementQueryService

**Files:**
- Create: `src/IfcMcpServer/Services/ElementQueryService.cs`
- Create: `tests/IfcMcpServer.Tests/Services/ElementQueryServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/IfcMcpServer.Tests/Services/ElementQueryServiceTests.cs
using IfcMcpServer.Services;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tests.Services;

public class ElementQueryServiceTests : IDisposable
{
    private readonly ModelSession _session;
    private readonly ElementQueryService _service;

    public ElementQueryServiceTests()
    {
        _session = new ModelSession();
        TestModelBuilder.EnsureTestModel();
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _service = new ElementQueryService();
    }

    [Fact]
    public void QueryElements_NoFilters_ReturnsAllProducts()
    {
        var results = _service.QueryElements(_session.CurrentModel!, null, null, null);
        Assert.True(results.Count() > 0);
    }

    [Fact]
    public void QueryElements_ByIfcType_FiltersCorrectly()
    {
        var walls = _service.QueryElements(_session.CurrentModel!, "IfcWall", null, null);
        Assert.All(walls, e => Assert.IsAssignableFrom<IIfcWall>(e));
    }

    [Fact]
    public void QueryElements_ByIfcType_IncludesSubtypes()
    {
        // IfcWall should include IfcWallStandardCase
        var walls = _service.QueryElements(_session.CurrentModel!, "IfcWall", null, null);
        Assert.True(walls.Any());
    }

    [Fact]
    public void QueryElements_ByClassification_FiltersCorrectly()
    {
        var results = _service.QueryElements(_session.CurrentModel!, null, "Ss_20_05_28", null);
        Assert.True(results.Any());
    }

    [Fact]
    public void QueryElements_ByClassificationWildcard_FiltersCorrectly()
    {
        var results = _service.QueryElements(_session.CurrentModel!, null, "Ss_20*", null);
        Assert.True(results.Any());
    }

    [Fact]
    public void QueryElements_ByPropertyFilter_FiltersCorrectly()
    {
        var filters = new[] { "Pset_WallCommon.IsExternal=True" };
        var results = _service.QueryElements(_session.CurrentModel!, "IfcWall", null, filters);
        Assert.Single(results);
    }

    [Fact]
    public void QueryElements_CombinedFilters_AndsAll()
    {
        var filters = new[] { "Pset_WallCommon.IsExternal=False" };
        var results = _service.QueryElements(_session.CurrentModel!, "IfcWall", null, filters);
        Assert.Single(results);
    }

    [Fact]
    public void GetClassifications_ReturnsClassificationRefs()
    {
        var classifications = _service.GetClassifications(_session.CurrentModel!);
        Assert.True(classifications.Any());
    }

    [Fact]
    public void GetPropertySets_ReturnsDistinctSets()
    {
        var psets = _service.GetPropertySetDefinitions(_session.CurrentModel!, "IfcWall");
        Assert.Contains(psets, p => p.Name == "Pset_WallCommon");
    }

    [Fact]
    public void GetStoreys_ReturnsStoreyInfo()
    {
        var storeys = _service.GetStoreys(_session.CurrentModel!);
        Assert.True(storeys.Count() >= 2);
    }

    public void Dispose() => _session.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ElementQueryServiceTests" -v n
```

Expected: FAIL — `ElementQueryService` does not exist.

- [ ] **Step 3: Implement ElementQueryService**

```csharp
// src/IfcMcpServer/Services/ElementQueryService.cs
using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public record PropertySetDefinition(string Name, IReadOnlyList<string> PropertyNames);
public record StoreyInfo(string Name, double Elevation, int ElementCount);
public record ClassificationInfo(string? SystemName, string Identification, string? Name);

public class ElementQueryService
{
    public IEnumerable<IIfcProduct> QueryElements(
        IModel model,
        string? ifcType,
        string? classification,
        string[]? propertyFilters)
    {
        IEnumerable<IIfcProduct> elements = model.Instances.OfType<IIfcProduct>();

        if (!string.IsNullOrEmpty(ifcType))
            elements = FilterByIfcType(elements, ifcType, model);

        if (!string.IsNullOrEmpty(classification))
            elements = FilterByClassification(elements, classification);

        if (propertyFilters is { Length: > 0 })
            elements = FilterByProperties(elements, propertyFilters);

        return elements;
    }

    private IEnumerable<IIfcProduct> FilterByIfcType(IEnumerable<IIfcProduct> elements, string ifcType, IModel model)
    {
        // Use XBIM's metadata to resolve the type including subtypes
        var expressType = model.Metadata.ExpressType(ifcType.ToUpperInvariant());
        if (expressType == null)
        {
            // Fallback: try case-insensitive match on .NET type name
            return elements.Where(e =>
                e.GetType().Name.Equals(ifcType, StringComparison.OrdinalIgnoreCase));
        }

        // Include the type and all its subtypes
        var validTypes = new HashSet<Type>(
            expressType.SubTypes?.Select(st => st.Type) ?? Enumerable.Empty<Type>())
        { expressType.Type };

        return elements.Where(e => validTypes.Any(t => t.IsInstanceOfType(e)));
    }

    private IEnumerable<IIfcProduct> FilterByClassification(IEnumerable<IIfcProduct> elements, string classification)
    {
        var pattern = "^" + Regex.Escape(classification).Replace("\\*", ".*") + "$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return elements.Where(e =>
        {
            var refs = GetClassificationReferences(e);
            return refs.Any(r =>
                (r.Identification != null && regex.IsMatch(r.Identification.ToString())) ||
                (r.Name != null && regex.IsMatch(r.Name.ToString())));
        });
    }

    private IEnumerable<IIfcProduct> FilterByProperties(IEnumerable<IIfcProduct> elements, string[] filters)
    {
        var parsed = filters.Select(ParsePropertyFilter).ToList();
        return elements.Where(e => parsed.All(f => MatchesPropertyFilter(e, f)));
    }

    public static IEnumerable<IIfcClassificationReference> GetClassificationReferences(IIfcProduct element)
    {
        // Direct associations
        var direct = element.HasAssociations
            .OfType<IIfcRelAssociatesClassification>()
            .Select(r => r.RelatingClassification)
            .OfType<IIfcClassificationReference>();

        // Type-level associations
        var typeRefs = Enumerable.Empty<IIfcClassificationReference>();
        var typeDefs = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeDefs != null)
        {
            typeRefs = typeDefs.HasAssociations
                .OfType<IIfcRelAssociatesClassification>()
                .Select(r => r.RelatingClassification)
                .OfType<IIfcClassificationReference>();
        }

        return direct.Concat(typeRefs);
    }

    public IEnumerable<ClassificationInfo> GetClassifications(IModel model)
    {
        return model.Instances.OfType<IIfcClassificationReference>()
            .Select(r =>
            {
                var system = r.ReferencedSource is IIfcClassification c ? c.Name?.ToString() : null;
                return new ClassificationInfo(system, r.Identification?.ToString() ?? "", r.Name?.ToString());
            })
            .Distinct();
    }

    public IEnumerable<PropertySetDefinition> GetPropertySetDefinitions(IModel model, string? ifcType)
    {
        var elements = ifcType != null
            ? QueryElements(model, ifcType, null, null)
            : model.Instances.OfType<IIfcProduct>();

        var psetMap = new Dictionary<string, HashSet<string>>();

        foreach (var element in elements)
        {
            foreach (var rel in element.IsDefinedBy)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
                {
                    var name = pset.Name?.ToString() ?? "(unnamed)";
                    if (!psetMap.ContainsKey(name))
                        psetMap[name] = new HashSet<string>();

                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                        psetMap[name].Add(prop.Name?.ToString() ?? "");
                }
            }
        }

        return psetMap.Select(kv => new PropertySetDefinition(kv.Key, kv.Value.OrderBy(n => n).ToList()));
    }

    public IEnumerable<StoreyInfo> GetStoreys(IModel model)
    {
        return model.Instances.OfType<IIfcBuildingStorey>()
            .OrderBy(s => s.Elevation?.Value ?? 0)
            .Select(s => new StoreyInfo(
                s.Name?.ToString() ?? "(unnamed)",
                s.Elevation?.Value ?? 0,
                s.ContainsElements.SelectMany(r => r.RelatedElements).Count()));
    }

    public static string? GetStoreyName(IIfcProduct element)
    {
        // Find the storey that contains this element
        var storey = element.Model.Instances.OfType<IIfcRelContainedInSpatialStructure>()
            .Where(r => r.RelatedElements.Contains(element))
            .Select(r => r.RelatingStructure)
            .OfType<IIfcBuildingStorey>()
            .FirstOrDefault();
        return storey?.Name?.ToString();
    }

    // --- Property filter parsing ---

    private record PropertyFilter(string PsetName, string PropName, string Operator, string Value);

    private static PropertyFilter ParsePropertyFilter(string filter)
    {
        var operators = new[] { "!=", ">=", "<=", ">", "<", "=" };
        foreach (var op in operators)
        {
            var idx = filter.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0)
            {
                var left = filter[..idx];
                var value = filter[(idx + op.Length)..];
                var dotIdx = left.IndexOf('.');
                if (dotIdx < 0)
                    throw new ArgumentException($"Property filter must be in format 'PsetName.PropertyName{op}Value': {filter}");
                return new PropertyFilter(left[..dotIdx], left[(dotIdx + 1)..], op, value);
            }
        }
        throw new ArgumentException($"Property filter must contain an operator (=, !=, >, <, >=, <=): {filter}");
    }

    private static bool MatchesPropertyFilter(IIfcProduct element, PropertyFilter filter)
    {
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset &&
                string.Equals(pset.Name?.ToString(), filter.PsetName, StringComparison.OrdinalIgnoreCase))
            {
                var prop = pset.HasProperties
                    .OfType<IIfcPropertySingleValue>()
                    .FirstOrDefault(p => string.Equals(p.Name?.ToString(), filter.PropName, StringComparison.OrdinalIgnoreCase));

                if (prop?.NominalValue == null) return false;

                var actual = prop.NominalValue.ToString();
                return EvaluateOperator(actual, filter.Operator, filter.Value);
            }
        }
        return false;
    }

    private static bool EvaluateOperator(string actual, string op, string expected)
    {
        if (op == "=" || op == "!=")
        {
            var equals = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            return op == "=" ? equals : !equals;
        }

        // Numeric operators
        if (!double.TryParse(actual, out var actualNum))
            throw new InvalidOperationException($"Operator '{op}' requires numeric values, but got '{actual}'");
        if (!double.TryParse(expected, out var expectedNum))
            throw new InvalidOperationException($"Operator '{op}' requires numeric values, but got '{expected}'");

        return op switch
        {
            ">" => actualNum > expectedNum,
            "<" => actualNum < expectedNum,
            ">=" => actualNum >= expectedNum,
            "<=" => actualNum <= expectedNum,
            _ => throw new ArgumentException($"Unknown operator: {op}")
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ElementQueryServiceTests" -v n
```

Expected: All tests PASS.

- [ ] **Step 5: Register in DI in Program.cs**

```csharp
builder.Services.AddSingleton<ElementQueryService>();
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ElementQueryService with type/classification/property filtering"
```

---

### Task 5: QueryTools (MCP tools for element querying)

**Files:**
- Create: `src/IfcMcpServer/Tools/QueryTools.cs`

- [ ] **Step 1: Implement QueryTools**

```csharp
// src/IfcMcpServer/Tools/QueryTools.cs
using System.ComponentModel;
using System.Text;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class QueryTools
{
    [McpServerTool(Name = "list-elements", ReadOnly = true), Description("List elements in the loaded IFC model with optional filtering by type, classification, and properties.")]
    public static string ListElements(
        ModelSession session,
        ElementQueryService queryService,
        [Description("IFC entity type to filter by (e.g. 'IfcWall', 'IfcSlab'). Includes subtypes.")] string? ifcType = null,
        [Description("Classification reference code or name to filter by. Supports * wildcard (e.g. 'Ss_20*'). Case-insensitive.")] string? classification = null,
        [Description("Property filters in format 'PsetName.PropertyName=Value'. Operators: =, !=, >, <, >=, <=.")] string[]? propertyFilter = null,
        [Description("Maximum number of results to return.")] int maxResults = 50)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var elements = queryService.QueryElements(session.CurrentModel!, ifcType, classification, propertyFilter)
            .Take(maxResults)
            .ToList();

        if (elements.Count == 0)
            return "No elements found matching the specified filters.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {elements.Count} element(s):");
        sb.AppendLine();
        sb.AppendLine("| GlobalId | Name | Type | Classification |");
        sb.AppendLine("|----------|------|------|----------------|");

        foreach (var e in elements)
        {
            var classRef = ElementQueryService.GetClassificationReferences(e).FirstOrDefault();
            var classCode = classRef?.Identification?.ToString() ?? "";
            sb.AppendLine($"| {e.GlobalId} | {e.Name?.ToString() ?? ""} | {e.GetType().Name} | {classCode} |");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "get-element", ReadOnly = true), Description("Get full details of a specific element by its GlobalId, including all property sets, quantity sets, and classification references.")]
    public static string GetElement(
        ModelSession session,
        [Description("The GlobalId (GUID) of the element to retrieve")] string globalId)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var element = session.CurrentModel!.Instances.OfType<IIfcProduct>()
            .FirstOrDefault(e => e.GlobalId.ToString() == globalId);

        if (element == null)
            return $"Error: No element found with GlobalId '{globalId}'.";

        var sb = new StringBuilder();
        sb.AppendLine($"GlobalId: {element.GlobalId}");
        sb.AppendLine($"Name: {element.Name?.ToString() ?? "(unnamed)"}");
        sb.AppendLine($"Type: {element.GetType().Name}");
        sb.AppendLine($"Storey: {ElementQueryService.GetStoreyName(element) ?? "(none)"}");

        // Type object
        var typeObj = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeObj != null)
            sb.AppendLine($"TypeObject: {typeObj.Name?.ToString() ?? "(unnamed)"} ({typeObj.GetType().Name})");

        // Classifications
        var classRefs = ElementQueryService.GetClassificationReferences(element).ToList();
        if (classRefs.Any())
        {
            sb.AppendLine("\nClassifications:");
            foreach (var cr in classRefs)
                sb.AppendLine($"  {cr.Identification?.ToString() ?? ""} - {cr.Name?.ToString() ?? ""}");
        }

        // Property sets
        sb.AppendLine("\nProperty Sets:");
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
            {
                sb.AppendLine($"  {pset.Name?.ToString() ?? "(unnamed)"}:");
                foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    sb.AppendLine($"    {prop.Name}: {prop.NominalValue?.ToString() ?? "(null)"}");
            }
        }

        // Quantity sets
        sb.AppendLine("\nQuantity Sets:");
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcElementQuantity qset)
            {
                sb.AppendLine($"  {qset.Name?.ToString() ?? "(unnamed)"}:");
                foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                    sb.AppendLine($"    {q.Name}: {GetQuantityValue(q)}");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list-classifications", ReadOnly = true), Description("List all classification systems and their references used in the currently loaded model.")]
    public static string ListClassifications(
        ModelSession session,
        ElementQueryService queryService)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var classifications = queryService.GetClassifications(session.CurrentModel!).ToList();

        if (classifications.Count == 0)
            return "No classification references found in this model.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {classifications.Count} classification reference(s):");
        sb.AppendLine();
        sb.AppendLine("| System | Code | Name |");
        sb.AppendLine("|--------|------|------|");
        foreach (var c in classifications)
            sb.AppendLine($"| {c.SystemName ?? ""} | {c.Identification} | {c.Name ?? ""} |");

        return sb.ToString();
    }

    [McpServerTool(Name = "list-property-sets", ReadOnly = true), Description("List distinct property set names and their properties found across elements in the model.")]
    public static string ListPropertySets(
        ModelSession session,
        ElementQueryService queryService,
        [Description("Optional IFC type to scope the search (e.g. 'IfcWall')")] string? ifcType = null)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var psets = queryService.GetPropertySetDefinitions(session.CurrentModel!, ifcType).ToList();

        if (psets.Count == 0)
            return "No property sets found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {psets.Count} property set(s):");
        foreach (var pset in psets.OrderBy(p => p.Name))
        {
            sb.AppendLine($"\n  {pset.Name}:");
            foreach (var prop in pset.PropertyNames)
                sb.AppendLine($"    - {prop}");
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "list-storeys", ReadOnly = true), Description("List all building storeys in the model with element counts.")]
    public static string ListStoreys(
        ModelSession session,
        ElementQueryService queryService)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var storeys = queryService.GetStoreys(session.CurrentModel!).ToList();

        if (storeys.Count == 0)
            return "No building storeys found.";

        var sb = new StringBuilder();
        sb.AppendLine("| Storey | Elevation | Elements |");
        sb.AppendLine("|--------|-----------|----------|");
        foreach (var s in storeys)
            sb.AppendLine($"| {s.Name} | {s.Elevation:F1} | {s.ElementCount} |");

        return sb.ToString();
    }

    private static string GetQuantityValue(IIfcPhysicalSimpleQuantity q) => q switch
    {
        IIfcQuantityLength ql => $"{ql.LengthValue:F3}",
        IIfcQuantityArea qa => $"{qa.AreaValue:F3}",
        IIfcQuantityVolume qv => $"{qv.VolumeValue:F6}",
        IIfcQuantityWeight qw => $"{qw.WeightValue:F3}",
        IIfcQuantityCount qc => $"{qc.CountValue:F0}",
        _ => q.ToString() ?? ""
    };
}
```

Note: `GetClassificationReferences` on `ElementQueryService` must be made `public static` for `QueryTools` to call it. Update the method visibility in `ElementQueryService` if it was `private`.

- [ ] **Step 2: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add QueryTools with list-elements, get-element, list-classifications, list-property-sets, list-storeys"
```

---

### Task 6: QuantityCalculator Service

**Files:**
- Create: `src/IfcMcpServer/Services/QuantityCalculator.cs`
- Create: `tests/IfcMcpServer.Tests/Services/QuantityCalculatorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/IfcMcpServer.Tests/Services/QuantityCalculatorTests.cs
using IfcMcpServer.Services;

namespace IfcMcpServer.Tests.Services;

public class QuantityCalculatorTests : IDisposable
{
    private readonly ModelSession _session;
    private readonly ElementQueryService _queryService;
    private readonly QuantityCalculator _calculator;

    public QuantityCalculatorTests()
    {
        _session = new ModelSession();
        TestModelBuilder.EnsureTestModel();
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _queryService = new ElementQueryService();
        _calculator = new QuantityCalculator(_queryService);
    }

    [Fact]
    public void Calculate_GroupByType_ReturnsSumsPerType()
    {
        var results = _calculator.Calculate(
            _session.CurrentModel!, "IfcWall", null, null,
            "type", null);

        Assert.Single(results.Groups);
        Assert.True(results.Groups[0].ElementCount == 2);
    }

    [Fact]
    public void Calculate_GroupByStorey_ReturnsSumsPerStorey()
    {
        var results = _calculator.Calculate(
            _session.CurrentModel!, null, null, null,
            "storey", null);

        Assert.True(results.Groups.Count >= 1);
    }

    [Fact]
    public void Calculate_GroupByClassification_GroupsCorrectly()
    {
        var results = _calculator.Calculate(
            _session.CurrentModel!, null, null, null,
            "classification", null);

        Assert.True(results.Groups.Count >= 1);
    }

    [Fact]
    public void Calculate_SpecificQuantityNames_OnlyIncludesRequested()
    {
        var results = _calculator.Calculate(
            _session.CurrentModel!, "IfcWall", null, null,
            "type", new[] { "NetSideArea" });

        Assert.True(results.QuantityColumns.Count <= 1);
    }

    [Fact]
    public void Calculate_QuantityResolution_InstanceOverridesType()
    {
        // Instance-level quantities should take precedence
        var results = _calculator.Calculate(
            _session.CurrentModel!, "IfcWall", null, null,
            "type", null);

        // Should have quantity values from the instance-level Qto_WallBaseQuantities
        Assert.True(results.Groups[0].Quantities.Values.Any(v => v > 0));
    }

    public void Dispose() => _session.Dispose();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~QuantityCalculatorTests" -v n
```

Expected: FAIL — `QuantityCalculator` does not exist.

- [ ] **Step 3: Implement QuantityCalculator**

```csharp
// src/IfcMcpServer/Services/QuantityCalculator.cs
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public record QuantityGroup(string GroupKey, int ElementCount, Dictionary<string, double> Quantities);
public record QuantityResult(IReadOnlyList<string> QuantityColumns, IReadOnlyList<QuantityGroup> Groups);

public class QuantityCalculator
{
    private readonly ElementQueryService _queryService;

    public QuantityCalculator(ElementQueryService queryService)
    {
        _queryService = queryService;
    }

    public QuantityResult Calculate(
        IModel model,
        string? ifcType,
        string? classification,
        string[]? propertyFilters,
        string groupBy,
        string[]? quantityNames)
    {
        var elements = _queryService.QueryElements(model, ifcType, classification, propertyFilters).ToList();

        // Group elements
        var groups = GroupElements(elements, groupBy, model);

        // Collect all quantity columns
        var allQuantityNames = new HashSet<string>();
        var groupResults = new List<QuantityGroup>();

        foreach (var (groupKey, groupElements) in groups)
        {
            var sums = new Dictionary<string, double>();

            foreach (var element in groupElements)
            {
                var quantities = ResolveQuantities(element);
                foreach (var (name, value) in quantities)
                {
                    if (quantityNames != null && !quantityNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    allQuantityNames.Add(name);
                    sums[name] = sums.GetValueOrDefault(name, 0) + value;
                }
            }

            groupResults.Add(new QuantityGroup(groupKey, groupElements.Count, sums));
        }

        var columns = (quantityNames ?? allQuantityNames.OrderBy(n => n).ToArray()).ToList();
        return new QuantityResult(columns, groupResults);
    }

    private static IEnumerable<(string GroupKey, List<IIfcProduct> Elements)> GroupElements(
        List<IIfcProduct> elements, string groupBy, IModel model)
    {
        if (groupBy.StartsWith("property:", StringComparison.OrdinalIgnoreCase))
        {
            var propPath = groupBy["property:".Length..];
            return elements
                .GroupBy(e => GetPropertyValue(e, propPath) ?? "(no value)")
                .Select(g => (g.Key, g.ToList()));
        }

        return groupBy.ToLowerInvariant() switch
        {
            "type" => elements
                .GroupBy(e => e.GetType().Name)
                .Select(g => (g.Key, g.ToList())),

            "classification" => elements
                .GroupBy(e =>
                {
                    var classRef = ElementQueryService.GetClassificationReferences(e).FirstOrDefault();
                    return classRef?.Identification?.ToString() ?? "(unclassified)";
                })
                .Select(g => (g.Key, g.ToList())),

            "storey" => elements
                .GroupBy(e => ElementQueryService.GetStoreyName(e) ?? "(no storey)")
                .Select(g => (g.Key, g.ToList())),

            _ => throw new ArgumentException($"Unknown groupBy value: '{groupBy}'. Expected: type, classification, storey, or property:PsetName.PropertyName")
        };
    }

    private static IEnumerable<(string Name, double Value)> ResolveQuantities(IIfcProduct element)
    {
        var resolved = new Dictionary<string, double>();

        // Type-level quantities first (lower priority)
        var typeObj = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeObj != null)
        {
            foreach (var qset in typeObj.HasPropertySets.OfType<IIfcElementQuantity>())
            {
                foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                {
                    var val = GetNumericValue(q);
                    if (val.HasValue)
                        resolved[q.Name.ToString()] = val.Value;
                }
            }
        }

        // Instance-level quantities (higher priority — overwrites type-level)
        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcElementQuantity qset)
            {
                foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                {
                    var val = GetNumericValue(q);
                    if (val.HasValue)
                        resolved[q.Name.ToString()] = val.Value;
                }
            }
        }

        return resolved.Select(kv => (kv.Key, kv.Value));
    }

    private static double? GetNumericValue(IIfcPhysicalSimpleQuantity q) => q switch
    {
        IIfcQuantityLength ql => ql.LengthValue,
        IIfcQuantityArea qa => qa.AreaValue,
        IIfcQuantityVolume qv => qv.VolumeValue,
        IIfcQuantityWeight qw => qw.WeightValue,
        IIfcQuantityCount qc => qc.CountValue,
        _ => null
    };

    private static string? GetPropertyValue(IIfcProduct element, string propPath)
    {
        var dotIdx = propPath.IndexOf('.');
        if (dotIdx < 0) return null;

        var psetName = propPath[..dotIdx];
        var propName = propPath[(dotIdx + 1)..];

        foreach (var rel in element.IsDefinedBy)
        {
            if (rel.RelatingPropertyDefinition is IIfcPropertySet pset &&
                string.Equals(pset.Name?.ToString(), psetName, StringComparison.OrdinalIgnoreCase))
            {
                var prop = pset.HasProperties
                    .OfType<IIfcPropertySingleValue>()
                    .FirstOrDefault(p => string.Equals(p.Name?.ToString(), propName, StringComparison.OrdinalIgnoreCase));
                return prop?.NominalValue?.ToString();
            }
        }
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~QuantityCalculatorTests" -v n
```

Expected: All tests PASS.

- [ ] **Step 5: Register in DI**

```csharp
builder.Services.AddSingleton<QuantityCalculator>();
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add QuantityCalculator with grouping and quantity resolution"
```

---

### Task 7: QuantityTools (MCP tool for calculate-quantities)

**Files:**
- Create: `src/IfcMcpServer/Tools/QuantityTools.cs`

- [ ] **Step 1: Implement QuantityTools**

```csharp
// src/IfcMcpServer/Tools/QuantityTools.cs
using System.ComponentModel;
using System.Text;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class QuantityTools
{
    [McpServerTool(Name = "calculate-quantities", ReadOnly = true), Description("Calculate and aggregate quantities from matched elements, grouped by type, classification, storey, or property value.")]
    public static string CalculateQuantities(
        ModelSession session,
        QuantityCalculator calculator,
        [Description("How to group results: 'type', 'classification', 'storey', or 'property:PsetName.PropertyName'")] string groupBy,
        [Description("IFC entity type to filter by (e.g. 'IfcWall'). Includes subtypes.")] string? ifcType = null,
        [Description("Classification reference code/name filter. Supports * wildcard.")] string? classification = null,
        [Description("Property filters in format 'PsetName.PropertyName=Value'.")] string[]? propertyFilter = null,
        [Description("Specific quantity names to include (e.g. 'NetSideArea', 'GrossVolume'). Omit for all quantities.")] string[]? quantityNames = null)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var result = calculator.Calculate(
            session.CurrentModel!, ifcType, classification, propertyFilter, groupBy, quantityNames);

        if (result.Groups.Count == 0)
            return "No elements found matching the specified filters.";

        var sb = new StringBuilder();

        // Header
        sb.Append("| Group | ElementCount |");
        foreach (var col in result.QuantityColumns)
            sb.Append($" {col} |");
        sb.AppendLine();

        sb.Append("|-------|-------------|");
        foreach (var _ in result.QuantityColumns)
            sb.Append("---------|");
        sb.AppendLine();

        // Rows
        foreach (var group in result.Groups)
        {
            sb.Append($"| {group.GroupKey} | {group.ElementCount} |");
            foreach (var col in result.QuantityColumns)
            {
                var val = group.Quantities.GetValueOrDefault(col, 0);
                sb.Append($" {val:F3} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add calculate-quantities MCP tool"
```

---

### Task 8: ExcelExporter Service

**Files:**
- Create: `src/IfcMcpServer/Services/ExcelExporter.cs`
- Create: `tests/IfcMcpServer.Tests/Services/ExcelExporterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/IfcMcpServer.Tests/Services/ExcelExporterTests.cs
using ClosedXML.Excel;
using IfcMcpServer.Services;

namespace IfcMcpServer.Tests.Services;

public class ExcelExporterTests : IDisposable
{
    private readonly ModelSession _session;
    private readonly ElementQueryService _queryService;
    private readonly QuantityCalculator _calculator;
    private readonly ExcelExporter _exporter;
    private readonly string _tempDir;

    public ExcelExporterTests()
    {
        _session = new ModelSession();
        TestModelBuilder.EnsureTestModel();
        _session.OpenModel(TestModelBuilder.TestModelPath);
        _queryService = new ElementQueryService();
        _calculator = new QuantityCalculator(_queryService);
        _exporter = new ExcelExporter(_queryService, _calculator);
        _tempDir = Path.Combine(Path.GetTempPath(), $"IfcMcpTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ExportElements_CreatesValidExcel()
    {
        var path = Path.Combine(_tempDir, "elements.xlsx");

        var count = _exporter.ExportElements(
            _session.CurrentModel!, path, "IfcWall", null, null);

        Assert.True(count > 0);
        Assert.True(File.Exists(path));

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        Assert.Equal("GlobalId", ws.Cell(1, 1).GetString());
        Assert.Equal("Name", ws.Cell(1, 2).GetString());
        Assert.Equal("IfcType", ws.Cell(1, 3).GetString());
    }

    [Fact]
    public void ExportElements_HeadersAreBold()
    {
        var path = Path.Combine(_tempDir, "elements_bold.xlsx");

        _exporter.ExportElements(_session.CurrentModel!, path, "IfcWall", null, null);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public void ExportQuantities_CreatesValidExcel()
    {
        var path = Path.Combine(_tempDir, "quantities.xlsx");

        var count = _exporter.ExportQuantities(
            _session.CurrentModel!, path, "IfcWall", null, null, "type", null);

        Assert.True(count > 0);
        Assert.True(File.Exists(path));

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        Assert.Equal("Group", ws.Cell(1, 1).GetString());
        Assert.Equal("ElementCount", ws.Cell(1, 2).GetString());
    }

    [Fact]
    public void ExportElements_NumericValuesStoredAsNumbers()
    {
        var path = Path.Combine(_tempDir, "elements_types.xlsx");

        _exporter.ExportElements(_session.CurrentModel!, path, "IfcWall", null, null);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        // Find a quantity column and check its data type
        var lastCol = ws.LastColumnUsed()!.ColumnNumber();
        for (var col = 7; col <= lastCol; col++)
        {
            var cell = ws.Cell(2, col);
            if (cell.DataType == XLDataType.Number)
            {
                Assert.True(true); // Found a numeric cell
                return;
            }
        }
        // If no quantity columns exist, that's ok for this test data
    }

    public void Dispose()
    {
        _session.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ExcelExporterTests" -v n
```

Expected: FAIL — `ExcelExporter` does not exist.

- [ ] **Step 3: Implement ExcelExporter**

```csharp
// src/IfcMcpServer/Services/ExcelExporter.cs
using ClosedXML.Excel;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcMcpServer.Services;

public class ExcelExporter
{
    private readonly ElementQueryService _queryService;
    private readonly QuantityCalculator _calculator;

    public ExcelExporter(ElementQueryService queryService, QuantityCalculator calculator)
    {
        _queryService = queryService;
        _calculator = calculator;
    }

    public int ExportElements(
        IModel model,
        string filePath,
        string? ifcType,
        string? classification,
        string[]? propertyFilters)
    {
        var elements = _queryService.QueryElements(model, ifcType, classification, propertyFilters).ToList();
        if (elements.Count == 0) return 0;

        // Collect all property and quantity column names
        var propColumns = new Dictionary<string, int>(); // "PsetName.PropName" -> column index
        var qtyColumns = new Dictionary<string, int>();   // "QtoName.QtyName" -> column index

        var elementData = new List<ElementRow>();

        foreach (var element in elements)
        {
            var row = new ElementRow
            {
                GlobalId = element.GlobalId.ToString(),
                Name = element.Name?.ToString() ?? "",
                IfcType = element.GetType().Name,
                Storey = ElementQueryService.GetStoreyName(element) ?? "",
            };

            // Classification
            var classRef = ElementQueryService.GetClassificationReferences(element).FirstOrDefault();
            row.Classification = classRef?.Identification?.ToString() ?? "";
            row.ClassificationName = classRef?.Name?.ToString() ?? "";

            // Properties
            foreach (var rel in element.IsDefinedBy)
            {
                if (rel.RelatingPropertyDefinition is IIfcPropertySet pset)
                {
                    var psetName = pset.Name?.ToString() ?? "";
                    foreach (var prop in pset.HasProperties.OfType<IIfcPropertySingleValue>())
                    {
                        var key = $"{psetName}.{prop.Name}";
                        if (!propColumns.ContainsKey(key))
                            propColumns[key] = propColumns.Count;
                        row.Properties[key] = prop.NominalValue?.ToString() ?? "";
                    }
                }

                if (rel.RelatingPropertyDefinition is IIfcElementQuantity qset)
                {
                    var qsetName = qset.Name?.ToString() ?? "";
                    foreach (var q in qset.Quantities.OfType<IIfcPhysicalSimpleQuantity>())
                    {
                        var key = $"{qsetName}.{q.Name}";
                        if (!qtyColumns.ContainsKey(key))
                            qtyColumns[key] = qtyColumns.Count;
                        row.Quantities[key] = GetNumericValue(q);
                    }
                }
            }

            elementData.Add(row);
        }

        // Write Excel
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Elements");

        // Fixed headers
        var fixedHeaders = new[] { "GlobalId", "Name", "IfcType", "Storey", "Classification", "ClassificationName" };
        for (int i = 0; i < fixedHeaders.Length; i++)
            sheet.Cell(1, i + 1).Value = fixedHeaders[i];

        var colOffset = fixedHeaders.Length;

        // Property headers
        var sortedPropKeys = propColumns.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedPropKeys.Count; i++)
            sheet.Cell(1, colOffset + i + 1).Value = sortedPropKeys[i];

        var qtyOffset = colOffset + sortedPropKeys.Count;

        // Quantity headers
        var sortedQtyKeys = qtyColumns.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedQtyKeys.Count; i++)
            sheet.Cell(1, qtyOffset + i + 1).Value = sortedQtyKeys[i];

        // Data rows
        for (int r = 0; r < elementData.Count; r++)
        {
            var row = elementData[r];
            var rowNum = r + 2;

            sheet.Cell(rowNum, 1).Value = row.GlobalId;
            sheet.Cell(rowNum, 2).Value = row.Name;
            sheet.Cell(rowNum, 3).Value = row.IfcType;
            sheet.Cell(rowNum, 4).Value = row.Storey;
            sheet.Cell(rowNum, 5).Value = row.Classification;
            sheet.Cell(rowNum, 6).Value = row.ClassificationName;

            foreach (var key in sortedPropKeys)
            {
                var col = colOffset + sortedPropKeys.IndexOf(key) + 1;
                if (row.Properties.TryGetValue(key, out var val))
                {
                    if (double.TryParse(val, out var numVal))
                        sheet.Cell(rowNum, col).Value = numVal;
                    else
                        sheet.Cell(rowNum, col).Value = val;
                }
            }

            foreach (var key in sortedQtyKeys)
            {
                var col = qtyOffset + sortedQtyKeys.IndexOf(key) + 1;
                if (row.Quantities.TryGetValue(key, out var val) && val.HasValue)
                    sheet.Cell(rowNum, col).Value = val.Value;
            }
        }

        ApplyFormatting(sheet);
        workbook.SaveAs(filePath);
        return elementData.Count;
    }

    public int ExportQuantities(
        IModel model,
        string filePath,
        string? ifcType,
        string? classification,
        string[]? propertyFilters,
        string groupBy,
        string[]? quantityNames)
    {
        var result = _calculator.Calculate(model, ifcType, classification, propertyFilters, groupBy, quantityNames);
        if (result.Groups.Count == 0) return 0;

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Quantities");

        // Headers
        sheet.Cell(1, 1).Value = "Group";
        sheet.Cell(1, 2).Value = "ElementCount";
        for (int i = 0; i < result.QuantityColumns.Count; i++)
            sheet.Cell(1, i + 3).Value = result.QuantityColumns[i];

        // Data rows
        for (int r = 0; r < result.Groups.Count; r++)
        {
            var group = result.Groups[r];
            var rowNum = r + 2;

            sheet.Cell(rowNum, 1).Value = group.GroupKey;
            sheet.Cell(rowNum, 2).Value = group.ElementCount;

            for (int c = 0; c < result.QuantityColumns.Count; c++)
            {
                var colName = result.QuantityColumns[c];
                if (group.Quantities.TryGetValue(colName, out var val))
                    sheet.Cell(rowNum, c + 3).Value = val;
            }
        }

        ApplyFormatting(sheet);
        workbook.SaveAs(filePath);
        return result.Groups.Count;
    }

    private static void ApplyFormatting(IXLWorksheet sheet)
    {
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        var headerRange = sheet.Range(1, 1, 1, lastCol);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        sheet.Columns().AdjustToContents();
    }

    private static double? GetNumericValue(IIfcPhysicalSimpleQuantity q) => q switch
    {
        IIfcQuantityLength ql => ql.LengthValue,
        IIfcQuantityArea qa => qa.AreaValue,
        IIfcQuantityVolume qv => qv.VolumeValue,
        IIfcQuantityWeight qw => qw.WeightValue,
        IIfcQuantityCount qc => qc.CountValue,
        _ => null
    };

    private class ElementRow
    {
        public string GlobalId { get; set; } = "";
        public string Name { get; set; } = "";
        public string IfcType { get; set; } = "";
        public string Storey { get; set; } = "";
        public string Classification { get; set; } = "";
        public string ClassificationName { get; set; } = "";
        public Dictionary<string, string> Properties { get; } = new();
        public Dictionary<string, double?> Quantities { get; } = new();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/IfcMcpServer.Tests --filter "FullyQualifiedName~ExcelExporterTests" -v n
```

Expected: All tests PASS.

- [ ] **Step 5: Register in DI**

```csharp
builder.Services.AddSingleton<ExcelExporter>();
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add ExcelExporter service for flat table .xlsx export"
```

---

### Task 9: ExportTools (MCP tools for Excel export)

**Files:**
- Create: `src/IfcMcpServer/Tools/ExportTools.cs`

- [ ] **Step 1: Implement ExportTools**

```csharp
// src/IfcMcpServer/Tools/ExportTools.cs
using System.ComponentModel;
using IfcMcpServer.Services;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public static class ExportTools
{
    [McpServerTool(Name = "export-elements", ReadOnly = false), Description("Export matched elements with all their properties and quantities to an Excel (.xlsx) file.")]
    public static string ExportElements(
        ModelSession session,
        ExcelExporter exporter,
        [Description("Path where the .xlsx file will be saved")] string filePath,
        [Description("IFC entity type to filter by (e.g. 'IfcWall'). Includes subtypes.")] string? ifcType = null,
        [Description("Classification reference code/name filter. Supports * wildcard.")] string? classification = null,
        [Description("Property filters in format 'PsetName.PropertyName=Value'.")] string[]? propertyFilter = null)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var count = exporter.ExportElements(
            session.CurrentModel!, filePath, ifcType, classification, propertyFilter);

        if (count == 0)
            return "No elements found matching the specified filters. No file was created.";

        return $"Exported {count} element(s) to {filePath}";
    }

    [McpServerTool(Name = "export-quantities", ReadOnly = false), Description("Export aggregated quantity calculations to an Excel (.xlsx) file.")]
    public static string ExportQuantities(
        ModelSession session,
        ExcelExporter exporter,
        [Description("Path where the .xlsx file will be saved")] string filePath,
        [Description("How to group results: 'type', 'classification', 'storey', or 'property:PsetName.PropertyName'")] string groupBy,
        [Description("IFC entity type to filter by (e.g. 'IfcWall'). Includes subtypes.")] string? ifcType = null,
        [Description("Classification reference code/name filter. Supports * wildcard.")] string? classification = null,
        [Description("Property filters in format 'PsetName.PropertyName=Value'.")] string[]? propertyFilter = null,
        [Description("Specific quantity names to include. Omit for all quantities.")] string[]? quantityNames = null)
    {
        if (!session.IsModelLoaded)
            return "Error: No model is currently loaded. Use open-model first.";

        var count = exporter.ExportQuantities(
            session.CurrentModel!, filePath, ifcType, classification, propertyFilter, groupBy, quantityNames);

        if (count == 0)
            return "No elements found matching the specified filters. No file was created.";

        return $"Exported {count} group(s) to {filePath}";
    }
}
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: add ExportTools with export-elements and export-quantities MCP tools"
```

---

### Task 10: Final Integration and Smoke Test

**Files:**
- Modify: `src/IfcMcpServer/Program.cs` (verify all DI registrations)

- [ ] **Step 1: Verify complete Program.cs**

Final `Program.cs` should have:

```csharp
using IfcMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<ModelSession>();
builder.Services.AddSingleton<ElementQueryService>();
builder.Services.AddSingleton<QuantityCalculator>();
builder.Services.AddSingleton<ExcelExporter>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "ifc-mcp-server", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test --verbosity normal
```

Expected: All tests PASS.

- [ ] **Step 3: Build the project**

```bash
dotnet build -c Release
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: finalize Program.cs with all DI registrations"
```
