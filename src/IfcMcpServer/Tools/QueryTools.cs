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

        var typeObj = element.IsTypedBy.FirstOrDefault()?.RelatingType;
        if (typeObj != null)
            sb.AppendLine($"TypeObject: {typeObj.Name?.ToString() ?? "(unnamed)"} ({typeObj.GetType().Name})");

        var classRefs = ElementQueryService.GetClassificationReferences(element).ToList();
        if (classRefs.Any())
        {
            sb.AppendLine("\nClassifications:");
            foreach (var cr in classRefs)
                sb.AppendLine($"  {cr.Identification?.ToString() ?? ""} - {cr.Name?.ToString() ?? ""}");
        }

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
