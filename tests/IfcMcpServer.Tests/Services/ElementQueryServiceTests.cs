using IfcMcpServer.Services;
using IfcMcpServer.Tests;
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
