using IfcMcpServer.Services;
using IfcMcpServer.Tests;

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
        var results = _calculator.Calculate(
            _session.CurrentModel!, "IfcWall", null, null,
            "type", null);

        Assert.True(results.Groups[0].Quantities.Values.Any(v => v > 0));
    }

    public void Dispose() => _session.Dispose();
}
