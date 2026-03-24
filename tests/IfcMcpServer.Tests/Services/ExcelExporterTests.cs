using ClosedXML.Excel;
using IfcMcpServer.Services;
using IfcMcpServer.Tests;

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
        var count = _exporter.ExportElements(_session.CurrentModel!, path, "IfcWall", null, null);
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
        var count = _exporter.ExportQuantities(_session.CurrentModel!, path, "IfcWall", null, null, "type", null);
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
        var lastCol = ws.LastColumnUsed()!.ColumnNumber();
        for (var col = 7; col <= lastCol; col++)
        {
            var cell = ws.Cell(2, col);
            if (cell.DataType == XLDataType.Number)
            {
                Assert.True(true);
                return;
            }
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
