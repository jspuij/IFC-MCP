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

        sb.Append("| Group | ElementCount |");
        foreach (var col in result.QuantityColumns)
            sb.Append($" {col} |");
        sb.AppendLine();

        sb.Append("|-------|-------------|");
        foreach (var _ in result.QuantityColumns)
            sb.Append("---------|");
        sb.AppendLine();

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
