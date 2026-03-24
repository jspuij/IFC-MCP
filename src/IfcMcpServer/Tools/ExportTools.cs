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
