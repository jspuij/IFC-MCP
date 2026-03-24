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
        IModel model, string filePath, string? ifcType, string? classification, string[]? propertyFilters)
    {
        var elements = _queryService.QueryElements(model, ifcType, classification, propertyFilters).ToList();
        if (elements.Count == 0) return 0;

        var propColumns = new Dictionary<string, int>();
        var qtyColumns = new Dictionary<string, int>();
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

            var classRef = ElementQueryService.GetClassificationReferences(element).FirstOrDefault();
            row.Classification = classRef?.Identification?.ToString() ?? "";
            row.ClassificationName = classRef?.Name?.ToString() ?? "";

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

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Elements");

        var fixedHeaders = new[] { "GlobalId", "Name", "IfcType", "Storey", "Classification", "ClassificationName" };
        for (int i = 0; i < fixedHeaders.Length; i++)
            sheet.Cell(1, i + 1).Value = fixedHeaders[i];

        var colOffset = fixedHeaders.Length;
        var sortedPropKeys = propColumns.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedPropKeys.Count; i++)
            sheet.Cell(1, colOffset + i + 1).Value = sortedPropKeys[i];

        var qtyOffset = colOffset + sortedPropKeys.Count;
        var sortedQtyKeys = qtyColumns.Keys.OrderBy(k => k).ToList();
        for (int i = 0; i < sortedQtyKeys.Count; i++)
            sheet.Cell(1, qtyOffset + i + 1).Value = sortedQtyKeys[i];

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
        IModel model, string filePath, string? ifcType, string? classification,
        string[]? propertyFilters, string groupBy, string[]? quantityNames)
    {
        var result = _calculator.Calculate(model, ifcType, classification, propertyFilters, groupBy, quantityNames);
        if (result.Groups.Count == 0) return 0;

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Quantities");

        sheet.Cell(1, 1).Value = "Group";
        sheet.Cell(1, 2).Value = "ElementCount";
        for (int i = 0; i < result.QuantityColumns.Count; i++)
            sheet.Cell(1, i + 3).Value = result.QuantityColumns[i];

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
